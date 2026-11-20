namespace Backup.Infra.Process;

/// <summary>
/// A program to run. Arguments are passed one by one to the program,
/// never through a shell, so no quoting or escaping is needed.
/// </summary>
/// <param name="Environment">Added to the inherited environment; a null value removes the variable.
/// Secrets go here, never in <paramref name="Arguments"/>.</param>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string?>? Environment = null,
    string? WorkingDirectory = null,
    string? StandardInput = null,
    bool CaptureStandardOutput = true);

/// <param name="StandardError">The last part of what the program wrote to its error output.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
