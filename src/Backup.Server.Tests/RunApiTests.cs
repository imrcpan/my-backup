using System.Net;
using System.Text.Json;
using Xunit;

namespace Backup.Server.Tests;

public sealed class RunApiTests : IDisposable
{
    private readonly ApiFactory _api = new();
    private string _group = "";
    private string _source = "";
    private string _sourcePath = "";

    public void Dispose() => _api.Dispose();

    private async Task<string> AddObject(string deviceId, string path, bool encrypted)
    {
        string password = encrypted ? ",\"password\":\"pw-" + Path.GetFileName(path) + "\"" : "";
        (HttpStatusCode status, JsonElement body) = await _api.Send("POST", $"/api/groups/{_group}/objects",
            $$"""{"deviceId":"{{deviceId}}","path":"{{path}}","encrypted":{{(encrypted ? "true" : "false")}}{{password}}}""");
        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("id").GetString()!;
    }

    private async Task Setup()
    {
        _group = (await _api.Send("POST", "/api/groups", """{"name":"Media"}""")).Body.GetProperty("id").GetString()!;
        _sourcePath = Directory.CreateDirectory(Path.Combine(_api.Root, "docs")).FullName;
        File.WriteAllText(Path.Combine(_sourcePath, "a.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_sourcePath, "sub"));
        File.WriteAllText(Path.Combine(_sourcePath, "sub/b.txt"), "world!");
        _source = await AddObject("local", _sourcePath, false);
    }

    private async Task<JsonElement> PlanSync(string? targets = null) =>
        (await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{_group}}","sourceObjectId":"{{_source}}"{{(targets == null ? "" : ",\"targetObjectIds\":" + targets)}}}""")).Body;

    [Fact]
    public async Task A_new_run_is_a_plan_that_changes_nothing_until_it_is_started()
    {
        await Setup();
        string repoPath = Path.Combine(_api.Cloud, "repo");
        string repo = await AddObject("gdrive", repoPath, true);

        JsonElement plan = await PlanSync();

        Assert.Equal("Planned", plan.GetProperty("state").GetString());
        Assert.Equal("Sync", plan.GetProperty("kind").GetString());
        JsonElement step = Assert.Single(plan.GetProperty("steps").EnumerateArray());
        Assert.Equal(repo, step.GetProperty("targetObjectId").GetString());
        Assert.Equal("ResticBackup", step.GetProperty("kind").GetString());
        Assert.Equal("Pending", step.GetProperty("state").GetString());
        Assert.Equal(2, step.GetProperty("toAdd").GetInt64());
        Assert.Contains("creates_repository", step.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        Assert.False(Directory.Exists(repoPath));
        Assert.DoesNotContain("pw-repo", plan.GetRawText());

        JsonElement again = (await _api.Send("GET", $"/api/runs/{plan.GetProperty("id").GetString()}")).Body;
        Assert.Equal("Planned", again.GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_confirmed_plan_runs_and_leaves_a_snapshot_a_log_and_a_history()
    {
        await Setup();
        string repo = await AddObject("gdrive", Path.Combine(_api.Cloud, "repo"), true);
        string id = (await PlanSync()).GetProperty("id").GetString()!;

        (HttpStatusCode started, JsonElement body) = await _api.Send("POST", $"/api/runs/{id}/start", """{"acknowledgeDestructive":false}""");
        Assert.Equal(HttpStatusCode.OK, started);
        Assert.Contains(body.GetProperty("state").GetString(), new[] { "Running", "Succeeded" });

        JsonElement done = await _api.WaitForRun(id);
        Assert.Equal("Succeeded", done.GetProperty("state").GetString());

        JsonElement snapshots = (await _api.Send("GET", $"/api/groups/{_group}/objects/{repo}/snapshots")).Body;
        JsonElement snapshot = Assert.Single(snapshots.EnumerateArray());
        Assert.Equal(2, snapshot.GetProperty("files").GetInt64());
        Assert.Equal([_sourcePath], snapshot.GetProperty("paths").EnumerateArray().Select(p => p.GetString()));

        string snapshotId = snapshot.GetProperty("id").GetString()!;
        JsonElement top = (await _api.Send("GET", $"/api/groups/{_group}/objects/{repo}/snapshots/{snapshotId}/files?path={Uri.EscapeDataString(_sourcePath)}")).Body;
        Assert.Equal(["sub", "a.txt"], top.EnumerateArray().Select(n => n.GetProperty("name").GetString()));
        Assert.Equal("dir", top[0].GetProperty("type").GetString());

        JsonElement log = (await _api.Send("GET", $"/api/runs/{id}/log")).Body;
        Assert.Contains(log.GetProperty("lines").EnumerateArray().Select(l => l.GetString()), l => l!.Contains("Run ended: Succeeded"));
        Assert.DoesNotContain("pw-repo", log.GetRawText());

        JsonElement history = (await _api.Send("GET", "/api/runs")).Body;
        Assert.Equal(id, history[0].GetProperty("id").GetString());
        Assert.Contains("Succeeded", await File.ReadAllTextAsync(Path.Combine(_api.Root, "data", "runs.json"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Targets_can_be_chosen_and_the_default_is_all_of_them()
    {
        await Setup();
        string a = await AddObject("gdrive", Path.Combine(_api.Cloud, "a"), true);
        string b = await AddObject("local", Path.Combine(_api.Root, "b"), true);
        string c = await AddObject("gdrive", Path.Combine(_api.Cloud, "c"), true);

        JsonElement all = await PlanSync();
        Assert.Equal([a, b, c], all.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("targetObjectId").GetString()));

        JsonElement some = await PlanSync($"[\"{c}\"]");
        Assert.Equal([c], some.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("targetObjectId").GetString()));

        (HttpStatusCode none, JsonElement body) = await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{_group}}","sourceObjectId":"{{_source}}","targetObjectIds":[]}""");
        Assert.Equal(HttpStatusCode.BadRequest, none);
        Assert.Equal("sync_targets_required", body.GetProperty("code").GetString());

        Assert.Equal("sync_source_required", (await _api.Send("POST", "/api/runs/sync", $$"""{"groupId":"{{_group}}"}""")).Body.GetProperty("code").GetString());
        Assert.Equal("group_not_found", (await _api.Send("POST", "/api/runs/sync", """{"groupId":"nope","sourceObjectId":"x"}""")).Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_plan_with_a_blocked_step_cannot_be_started()
    {
        await Setup();
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");
        await AddObject("media", "/backup", false);
        _api.Probe.Status = new(Backup.Infra.Disks.DiskState.NotConnected);

        JsonElement plan = await PlanSync();
        JsonElement step = plan.GetProperty("steps")[0];
        Assert.Equal("Blocked", step.GetProperty("state").GetString());
        Assert.Equal("disk_not_connected", step.GetProperty("errorCode").GetString());

        (HttpStatusCode status, JsonElement body) = await _api.Send("POST", $"/api/runs/{plan.GetProperty("id").GetString()}/start", """{"acknowledgeDestructive":true}""");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("run_blocked", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Removing_snapshots_needs_an_explicit_acknowledgement_and_removes_what_was_planned()
    {
        await Setup();
        string repo = await AddObject("gdrive", Path.Combine(_api.Cloud, "repo"), true);
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_sourcePath, "a.txt"), $"version {i}");
            string run = (await PlanSync()).GetProperty("id").GetString()!;
            await _api.Send("POST", $"/api/runs/{run}/start", """{"acknowledgeDestructive":false}""");
            Assert.Equal("Succeeded", (await _api.WaitForRun(run)).GetProperty("state").GetString());
        }

        string url = $"/api/groups/{_group}/objects/{repo}";
        await _api.Send("PUT", url + "/retention", """{"policy":{"keepLast":1}}""");
        JsonElement plan = (await _api.Send("POST", "/api/runs/forget", $$"""{"groupId":"{{_group}}","objectId":"{{repo}}","prune":true}""")).Body;
        JsonElement step = plan.GetProperty("steps")[0];
        Assert.Equal(2, step.GetProperty("toDelete").GetInt64());
        Assert.True(plan.GetProperty("destructive").GetBoolean());
        string id = plan.GetProperty("id").GetString()!;

        (HttpStatusCode refused, JsonElement refusedBody) = await _api.Send("POST", $"/api/runs/{id}/start", """{"acknowledgeDestructive":false}""");
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Equal("run_acknowledge_required", refusedBody.GetProperty("code").GetString());
        Assert.Equal(3, (await _api.Send("GET", url + "/snapshots")).Body.GetArrayLength());

        await _api.Send("POST", $"/api/runs/{id}/start", """{"acknowledgeDestructive":true}""");
        Assert.Equal("Succeeded", (await _api.WaitForRun(id)).GetProperty("state").GetString());
        Assert.Equal(1, (await _api.Send("GET", url + "/snapshots")).Body.GetArrayLength());
    }

    [Fact]
    public async Task A_restore_goes_to_a_new_directory_and_never_over_existing_files()
    {
        await Setup();
        string repo = await AddObject("gdrive", Path.Combine(_api.Cloud, "repo"), true);
        string sync = (await PlanSync()).GetProperty("id").GetString()!;
        await _api.Send("POST", $"/api/runs/{sync}/start", """{"acknowledgeDestructive":false}""");
        await _api.WaitForRun(sync);
        string snapshot = (await _api.Send("GET", $"/api/groups/{_group}/objects/{repo}/snapshots")).Body[0].GetProperty("id").GetString()!;

        string busy = Directory.CreateDirectory(Path.Combine(_api.Root, "busy")).FullName;
        File.WriteAllText(Path.Combine(busy, "mine.txt"), "x");
        JsonElement blocked = (await _api.Send("POST", "/api/runs/restore", $$"""{"groupId":"{{_group}}","objectId":"{{repo}}","snapshotId":"{{snapshot}}","targetDirectory":"{{busy}}"}""")).Body;
        Assert.Equal("restore_target_not_empty", blocked.GetProperty("steps")[0].GetProperty("errorCode").GetString());

        string target = Path.Combine(_api.Root, "restored");
        JsonElement plan = (await _api.Send("POST", "/api/runs/restore", $$"""{"groupId":"{{_group}}","objectId":"{{repo}}","snapshotId":"{{snapshot}}","targetDirectory":"{{target}}"}""")).Body;
        string id = plan.GetProperty("id").GetString()!;
        await _api.Send("POST", $"/api/runs/{id}/start", """{"acknowledgeDestructive":false}""");
        Assert.Equal("Succeeded", (await _api.WaitForRun(id)).GetProperty("state").GetString());
        Assert.Equal("hello", File.ReadAllText(Path.Combine(target, _sourcePath.TrimStart('/'), "a.txt")));
    }

    [Fact]
    public async Task A_check_runs_and_a_plan_can_be_discarded()
    {
        await Setup();
        string repo = await AddObject("gdrive", Path.Combine(_api.Cloud, "repo"), true);
        string sync = (await PlanSync()).GetProperty("id").GetString()!;
        await _api.Send("POST", $"/api/runs/{sync}/start", """{"acknowledgeDestructive":false}""");
        await _api.WaitForRun(sync);

        string check = (await _api.Send("POST", "/api/runs/check", $$"""{"groupId":"{{_group}}","objectId":"{{repo}}","depth":"Subset","subsetPercent":50}""")).Body.GetProperty("id").GetString()!;
        await _api.Send("POST", $"/api/runs/{check}/start", """{"acknowledgeDestructive":false}""");
        Assert.Equal("Succeeded", (await _api.WaitForRun(check)).GetProperty("state").GetString());

        string spare = (await PlanSync()).GetProperty("id").GetString()!;
        Assert.Equal("Discarded", (await _api.Send("POST", $"/api/runs/{spare}/discard")).Body.GetProperty("state").GetString());
        Assert.Equal("run_not_planned", (await _api.Send("POST", $"/api/runs/{spare}/start", """{"acknowledgeDestructive":true}""")).Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_runs_are_not_found_and_a_run_that_is_not_going_cannot_be_cancelled()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("GET", "/api/runs/nope")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("GET", "/api/runs/nope/log")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("POST", "/api/runs/nope/start", """{"acknowledgeDestructive":true}""")).Status);
        (HttpStatusCode status, JsonElement body) = await _api.Send("POST", "/api/runs/nope/cancel");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("run_not_running", body.GetProperty("code").GetString());
    }
}
