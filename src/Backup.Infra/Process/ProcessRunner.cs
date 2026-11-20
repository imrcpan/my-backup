using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Backup.Infra.Process;

public sealed class ProcessRunner : IProcessRunner
{
    private const int MaxErrorCharacters = 32 * 1024;

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

        using CancellationTokenRegistration kill = cancellationToken.Register(() => Kill(process));

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
