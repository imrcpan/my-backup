using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Backup.Infra.Process;

/// <param name="gracePeriod">
/// How long a program that is asked to stop may take to finish by itself, before it and its children are killed.
/// It is asked with SIGINT, as Ctrl+C does, and after a quarter of the time with SIGTERM, for the case that SIGINT is ignored
/// (a program that was started in the background by a shell inherits that). restic and rclone use the request to release
/// their locks and to remove the files they have only half written; a kill leaves those behind.
/// </param>
public sealed partial class ProcessRunner(TimeSpan? gracePeriod = null) : IProcessRunner
{
    private const int MaxErrorCharacters = 32 * 1024;
    private const int SigInt = 2;
    private const int SigTerm = 15;
    private readonly TimeSpan _gracePeriod = gracePeriod ?? TimeSpan.FromSeconds(20);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);

    public async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        ProcessStartInfo info = new(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = spec.StandardInput != null,
            CreateNoWindow = true,
            WorkingDirectory = spec.WorkingDirectory ?? "",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (spec.Environment != null)
        {
            foreach ((string key, string? value) in spec.Environment)
            {
                if (value == null)
                {
                    info.Environment.Remove(key);
                }
                else
                {
                    info.Environment[key] = value;
                }
            }
        }

        using System.Diagnostics.Process process = new() { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new ExternalToolException("tool_not_started", $"The program '{spec.FileName}' could not be started.");
        }

        using CancellationTokenSource exited = new();
        using CancellationTokenRegistration stop = cancellationToken.Register(() => Stop(process, exited.Token));

        StringBuilder output = new();
        Queue<string> errorLines = new();
        int errorLength = 0;

        Task outputTask = ReadLinesAsync(process.StandardOutput, line =>
        {
            if (spec.CaptureStandardOutput)
            {
                output.AppendLine(line);
            }

            onStandardOutputLine?.Invoke(line);
        });
        Task errorTask = ReadLinesAsync(process.StandardError, line =>
        {
            errorLines.Enqueue(line);
            errorLength += line.Length + 1;
            while (errorLength > MaxErrorCharacters && errorLines.Count > 1)
            {
                errorLength -= errorLines.Dequeue().Length + 1;
            }

            onStandardErrorLine?.Invoke(line);
        });

        if (spec.StandardInput != null)
        {
            try
            {
                await process.StandardInput.WriteAsync(spec.StandardInput);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The program ended before it read its input; its exit code tells the rest.
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await exited.CancelAsync();
        await Task.WhenAll(outputTask, errorTask);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output.ToString(), string.Join('\n', errorLines));
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is string line)
        {
            onLine(line);
        }
    }

    /// <summary>Asks the program to stop as Ctrl+C does, and kills it with its children if it has not ended in time.</summary>
    private void Stop(System.Diagnostics.Process process, CancellationToken ended)
    {
        if (OperatingSystem.IsWindows())
        {
            Kill(process);
            return;
        }

        try
        {
            if (SendSignal(process.Id, SigInt) != 0)
            {
                Kill(process);
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return; // Already finished.
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_gracePeriod / 4, ended);
                if (SendSignal(process.Id, SigTerm) != 0)
                {
                    Kill(process);
                    return;
                }

                await Task.Delay(_gracePeriod - (_gracePeriod / 4), ended);
                Kill(process);
            }
            catch (OperationCanceledException)
            {
                // It ended by itself.
            }
            catch (InvalidOperationException)
            {
                // Already finished.
            }
        }, CancellationToken.None);
    }

    private static void Kill(System.Diagnostics.Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already finished.
        }
    }
}
