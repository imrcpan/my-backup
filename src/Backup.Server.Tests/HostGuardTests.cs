using System.Net;
using Backup.Server.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Backup.Server.Tests;

public class HostGuardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HostGuardTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("localhost:5174", true)]
    [InlineData("LOCALHOST:1", true)]
    [InlineData("127.0.0.1:5174", true)]
    [InlineData("[::1]:5174", true)]
    [InlineData("evil.example.com", false)]
    [InlineData("localhost.evil.com", false)]
    [InlineData("127.0.0.1.evil.com:80", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_this_computer_is_accepted_as_the_host(string? host, bool expected)
    {
        Assert.Equal(expected, HostGuard.IsLocalHost(host));
    }

    [Theory]
    [InlineData("GET", "https://evil.example.com", "localhost:5174", true)]
    [InlineData("POST", null, "localhost:5174", true)]
    [InlineData("POST", "http://localhost:5174", "localhost:5174", true)]
    [InlineData("POST", "https://evil.example.com", "localhost:5174", false)]
    [InlineData("DELETE", "http://localhost:9999", "localhost:5174", false)]
    [InlineData("PUT", "null", "localhost:5174", false)]
    public void A_change_must_come_from_this_origin_or_from_none(string method, string? origin, string host, bool expected)
    {
        Assert.Equal(expected, HostGuard.IsAllowedOrigin(method, origin, host));
    }

    [Fact]
    public async Task The_server_answers_to_localhost_and_refuses_other_hosts_and_foreign_origins()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage ok = await client.GetAsync("/api/ping", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await ok.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using HttpRequestMessage rebinding = new(HttpMethod.Get, "/api/ping");
        rebinding.Headers.Host = "evil.example.com";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(rebinding, TestContext.Current.CancellationToken)).StatusCode);

        using HttpRequestMessage cross = new(HttpMethod.Post, "/api/ping");
        cross.Headers.Add("Origin", "https://evil.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(cross, TestContext.Current.CancellationToken)).StatusCode);
    }
}
