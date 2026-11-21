using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Backup.Server.Tests;

public sealed class BadRequestTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(HttpStatusCode Status, string Body)> Post(string url, string? body, string contentType = "application/json")
    {
        using HttpClient client = _api.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, url);
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"name\": 5}")]
    [InlineData("null")]
    public async Task A_body_that_is_not_what_the_endpoint_expects_is_a_bad_request_with_a_code_and_not_a_crash(string body)
    {
        (HttpStatusCode status, string text) = await Post("/api/groups", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        using JsonDocument error = JsonDocument.Parse(text);
        Assert.Equal("bad_request", error.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task A_missing_body_is_a_bad_request_and_a_wrong_content_type_is_refused_as_such()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/groups", null)).Status);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Post("/api/groups", "{\"name\":\"x\"}", "text/plain")).Status);
        // Nothing was made by the refused requests.
        Assert.Equal(0, (await _api.Send("GET", "/api/groups")).Body.GetArrayLength());
    }

    [Fact]
    public async Task An_enum_that_is_unknown_and_an_overlong_value_are_refused_not_crashed()
    {
        (HttpStatusCode kind, string kindBody) = await Post("/api/devices", """{"id":"x","name":"X","kind":"Floppy"}""");
        Assert.Equal(HttpStatusCode.BadRequest, kind);
        Assert.Contains("bad_request", kindBody);

        (HttpStatusCode name, string nameBody) = await Post("/api/groups", "{\"name\":\"" + new string('n', 5000) + "\"}");
        Assert.Equal(HttpStatusCode.BadRequest, name);
        Assert.Contains("group_name_invalid", nameBody);
    }
}
