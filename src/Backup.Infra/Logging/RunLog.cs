using System.Text.RegularExpressions;

namespace Backup.Infra.Logging;

public interface IRunLog
{
    /// <summary>Adds a line to the log of a run, after the redactor has masked the secrets in it.</summary>
    void Append(string runId, string line, SecretRedactor redactor);

    /// <summary>The last lines of the log of a run; empty if there is none.</summary>
    IReadOnlyList<string> Tail(string runId, int maxLines);
}

/// <summary>One text file per run. Nothing reaches the file without passing a redactor.</summary>
public sealed partial class RunLog(string directory, TimeProvider? time = null) : IRunLog
{
    private readonly object _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
    private static partial Regex IdPattern();

    private string PathOf(string runId) => IdPattern().IsMatch(runId)
        ? Path.Combine(directory, runId + ".log")
        : throw new ArgumentException("The run id is not valid.", nameof(runId));

    public void Append(string runId, string line, SecretRedactor redactor)
    {
        string path = PathOf(runId);
        string text = redactor.Redact(line).ReplaceLineEndings(" ");
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"{_time.GetUtcNow():yyyy-MM-dd HH:mm:ss}Z {text}{Environment.NewLine}");
        }
    }

    public IReadOnlyList<string> Tail(string runId, int maxLines)
    {
        string path = PathOf(runId);
        lock (_gate)
        {
            return File.Exists(path) ? [.. File.ReadLines(path).TakeLast(maxLines)] : [];
        }
    }
}
