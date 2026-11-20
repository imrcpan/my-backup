namespace Backup.Infra.Process;

public interface IProcessRunner
{
    /// <summary>
    /// Runs the program to its end. Each line is passed to the callbacks as soon as
    /// it is written. On cancellation the program and its children are killed and
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    /// <exception cref="ExternalToolException">The program could not be started.</exception>
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default);
}
