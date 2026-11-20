using System.Text.RegularExpressions;

namespace Backup.Infra.Logging;

/// <summary>
/// Removes secrets from text before it is logged or shown. It knows the exact
/// secrets it is given (repository passwords) and the usual shapes of tokens
/// (JSON fields, KEY=value pairs, bearer headers), as rclone can print those.
/// </summary>
public sealed partial class SecretRedactor
{
    public const string Mask = "***";

    private readonly string[] _secrets;

    public SecretRedactor(IEnumerable<string?> secrets)
    {
        _secrets = [.. secrets
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct()
            .OrderByDescending(s => s.Length)];
    }

    public static SecretRedactor None { get; } = new([]);

    [GeneratedRegex("""("(?:[A-Za-z_]*(?:password|passwd|token|secret|pass|key)[A-Za-z_]*)"\s*:\s*")(?:[^"\\]|\\.)*(")""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonField();

    [GeneratedRegex(@"\b([A-Za-z_]*(?:PASSWORD|PASSWD|TOKEN|SECRET|PASS|KEY)[A-Za-z_]*=)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Assignment();

    [GeneratedRegex(@"(\bBearer\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        string result = text;
        foreach (string secret in _secrets)
        {
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }

        result = JsonField().Replace(result, "$1" + Mask + "$2");
        result = Assignment().Replace(result, "$1" + Mask);
        return Bearer().Replace(result, "$1" + Mask);
    }
}
