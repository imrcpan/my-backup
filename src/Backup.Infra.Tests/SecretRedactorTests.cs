using Backup.Infra.Logging;
using Xunit;

namespace Backup.Infra.Tests;

public class SecretRedactorTests
{
    [Fact]
    public void Known_secrets_are_masked_everywhere()
    {
        SecretRedactor redactor = new(["hunter2", "hunter2-longer"]);
        Assert.Equal("a *** b *** c ***", redactor.Redact("a hunter2 b hunter2-longer c hunter2"));
    }

    [Fact]
    public void Longer_secret_wins_over_its_prefix()
    {
        SecretRedactor redactor = new(["abcd", "abcdef"]);
        Assert.Equal("***", redactor.Redact("abcdef"));
    }

    [Fact]
    public void Json_token_fields_are_masked()
    {
        const string text = """{"access_token":"ya29.a0Af","token_type":"Bearer","refresh_token":"1//0gXy","expiry":"2026-01-01"}""";
        string result = SecretRedactor.None.Redact(text);
        Assert.DoesNotContain("ya29", result);
        Assert.DoesNotContain("1//0gXy", result);
        Assert.Contains("\"expiry\":\"2026-01-01\"", result);
    }

    [Fact]
    public void Assignments_and_bearer_headers_are_masked()
    {
        string result = SecretRedactor.None.Redact("RESTIC_PASSWORD=abc123 RCLONE_CONFIG_PASS=zzz Authorization: Bearer ey.J0.sig");
        Assert.Equal("RESTIC_PASSWORD=*** RCLONE_CONFIG_PASS=*** Authorization: Bearer ***", result);
    }

    [Fact]
    public void Plain_text_is_unchanged_and_null_is_safe()
    {
        Assert.Equal("scanned 12 files", SecretRedactor.None.Redact("scanned 12 files"));
        Assert.Equal("", SecretRedactor.None.Redact(null));
        Assert.Equal("x", new SecretRedactor([null, ""]).Redact("x"));
    }
}
