using Backup.Infra.Process;

namespace Backup.Infra.Tests;

/// <summary>Remembers what it was asked to run and answers from a queue instead of running anything.</summary>
public sealed class RecordingRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _answers = new();

    public List<ProcessSpec> Specs { get; } = [];

    public void Answer(int exitCode, string output = "", string error = "") => _answers.Enqueue(new ProcessResult(exitCode, output, error));

    public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string>? onStandardOutputLine = null, Action<string>? onStandardErrorLine = null, CancellationToken cancellationToken = default)
    {
        Specs.Add(spec);
        ProcessResult result = _answers.Count > 0 ? _answers.Dequeue() : new ProcessResult(0, "", "");
        foreach (string line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            onStandardOutputLine?.Invoke(line);
        }

        return Task.FromResult(result);
    }
}
