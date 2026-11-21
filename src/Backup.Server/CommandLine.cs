namespace Backup.Server;

/// <summary>The arguments of the program. The port can be given as -p 1234, -p=1234, --port 1234 or --port=1234.</summary>
public static class CommandLine
{
    /// <summary>Rewrites the port options into the --Port=1234 form that the configuration reads; other arguments are kept.</summary>
    public static string[] Normalize(string[] args)
    {
        List<string> result = [];
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? name = arg.Split('=', 2)[0];
            if (!string.Equals(name, "-p", StringComparison.Ordinal) && !string.Equals(name, "--port", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(arg);
            }
            else if (arg.Contains('='))
            {
                result.Add("--Port=" + arg.Split('=', 2)[1]);
            }
            else
            {
                // The value is the next argument; a missing one is reported as an invalid port.
                result.Add("--Port=" + (i + 1 < args.Length ? args[++i] : ""));
            }
        }

        return [.. result];
    }

    /// <summary>The port to listen on, or an error text if the setting is not a port number.</summary>
    public static bool TryGetPort(string? setting, int fallback, out int port, out string? error)
    {
        error = null;
        if (setting == null)
        {
            port = fallback;
            return true;
        }

        if (int.TryParse(setting, out port) && port is >= 1 and <= 65535)
        {
            return true;
        }

        error = $"'{setting}' is not a port number. Use a number from 1 to 65535, for example: --port 1580";
        return false;
    }
}
