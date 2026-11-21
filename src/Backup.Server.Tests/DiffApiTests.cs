using System.Net;
using System.Text.Json;
using Xunit;

namespace Backup.Server.Tests;

public sealed class DiffApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(string Group, string Source, string Target)> Setup(int files, string[] stale)
    {
        string source = Directory.CreateDirectory(Path.Combine(_api.Root, "src")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(_api.Root, "dst")).FullName;
        for (int i = 0; i < files; i++)
        {
            File.WriteAllText(Path.Combine(source, $"f{i:D4}.txt"), "x");
        }

        foreach (string name in stale)
        {
            File.WriteAllText(Path.Combine(target, name), "stale");
        }

        string group = (await _api.Send("POST", "/api/groups", """{"name":"G"}""")).Body.GetProperty("id").GetString()!;
        string a = (await _api.Send("POST", $"/api/groups/{group}/objects", $$"""{"deviceId":"local","path":"{{source}}","encrypted":false}""")).Body.GetProperty("id").GetString()!;
        await _api.Send("POST", $"/api/groups/{group}/objects", $$"""{"deviceId":"local","path":"{{target}}","encrypted":false}""");
        return (group, a, target);
    }

    private async Task<(HttpStatusCode Status, string? Type, string? Disposition, string Body)> Get(string url)
    {
        using HttpClient client = _api.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentDisposition?.ToString(),
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_complete_diff_is_a_text_file_to_save_with_the_deletions_first_and_every_file_in_it()
    {
        (string group, string source, _) = await Setup(700, ["old-b.txt", "old-a.txt"]);
        JsonElement plan = (await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{group}}","sourceObjectId":"{{source}}"}""")).Body;
        JsonElement step = plan.GetProperty("steps")[0];
        Assert.True(step.GetProperty("hasDiff").GetBoolean());
        Assert.Equal(500, step.GetProperty("lines").GetArrayLength());
        Assert.True(step.GetProperty("linesTruncated").GetBoolean());
        Assert.Equal(10, step.GetProperty("bytesToDelete").GetInt64());
        Assert.False(step.TryGetProperty("completeDiff", out _), "the whole list is not part of the plan that is sent");

        (HttpStatusCode status, string? type, string? disposition, string body) =
            await Get($"/api/runs/{plan.GetProperty("id").GetString()}/steps/{step.GetProperty("id").GetString()}/diff");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("text/plain", type);
        Assert.StartsWith("attachment", disposition);
        Assert.Contains("my_backup_diff_", disposition);
        Assert.EndsWith(".txt", disposition!.TrimEnd('"'));
        string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToArray();
        Assert.Equal(2 + 700, lines.Length);
        Assert.Equal(["-\t5\told-a.txt", "-\t5\told-b.txt"], lines.Take(2));
        Assert.Equal("+\t1\tf0000.txt", lines[2]);
        Assert.Equal("+\t1\tf0699.txt", lines[^1]);
        Assert.StartsWith("# Run ", body);
    }

    [Fact]
    public async Task Unknown_runs_steps_and_steps_without_a_diff_are_not_found()
    {
        (string group, string source, string target) = await Setup(1, []);
        await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{group}}","sourceObjectId":"{{source}}"}""");
        // After that run copies its file, a second plan has nothing to do and so no diff.
        File.Copy(Path.Combine(_api.Root, "src", "f0000.txt"), Path.Combine(target, "f0000.txt"));
        JsonElement second = (await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{group}}","sourceObjectId":"{{source}}"}""")).Body;
        File.SetLastWriteTimeUtc(Path.Combine(target, "f0000.txt"), File.GetLastWriteTimeUtc(Path.Combine(_api.Root, "src", "f0000.txt")));
        JsonElement quiet = (await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{group}}","sourceObjectId":"{{source}}"}""")).Body;
        string quietStep = quiet.GetProperty("steps")[0].GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await Get("/api/runs/nope/steps/x/diff")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/runs/{second.GetProperty("id").GetString()}/steps/nope/diff")).Status);
        (HttpStatusCode status, _, _, string body) = await Get($"/api/runs/{quiet.GetProperty("id").GetString()}/steps/{quietStep}/diff");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("diff_not_found", body);
    }

    [Fact]
    public async Task A_step_id_that_is_a_path_cannot_reach_other_files()
    {
        (string group, string source, _) = await Setup(1, []);
        string run = (await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{group}}","sourceObjectId":"{{source}}"}""")).Body.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/runs/{run}/steps/..%2F..%2Fstate/diff")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/runs/..%2Fx/steps/y/diff")).Status);
    }
}
