namespace Backup.Core;

/// <summary>
/// Normal form of an absolute path on a storage device, so that
/// "/a/b" and "/a/b/" name the same place.
/// </summary>
public static class StoragePath
{
    public static string Normalize(string? input)
    {
        string trimmed = (input ?? "").Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/' || trimmed.Contains('\0'))
        {
            throw new DomainException("path_invalid", "The path must be absolute and start with '/'.");
        }

        List<string> parts = [];
        foreach (string part in trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                throw new DomainException("path_invalid", "The path must not contain '..'.");
            }

            parts.Add(part);
        }

        return "/" + string.Join('/', parts);
    }
}
