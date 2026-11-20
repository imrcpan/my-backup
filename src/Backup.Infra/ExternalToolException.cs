namespace Backup.Infra;

/// <summary>
/// A program outside this application could not be started or failed.
/// The message never contains secrets; <see cref="Code"/> is a stable key.
/// </summary>
public sealed class ExternalToolException(string code, string message, int? exitCode = null) : Exception(message)
{
    public string Code { get; } = code;

    public int? ExitCode { get; } = exitCode;
}
