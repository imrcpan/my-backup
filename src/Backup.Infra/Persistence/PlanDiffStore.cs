using System.Text;
using System.Text.RegularExpressions;
using Backup.Core.Runs;

namespace Backup.Infra.Persistence;

public interface IPlanDiffStore
{
    /// <summary>Writes the whole list of what a step does, so that it can be downloaded.</summary>
    void Write(string runId, string stepId, string header, IEnumerable<PlanLine> lines);

    /// <summary>The file of a step to read, or null if there is none.</summary>
    Stream? Open(string runId, string stepId);

    /// <summary>Removes the files of every run that is not in <paramref name="runIds"/>, so that they do not pile up.</summary>
    void Keep(IReadOnlyCollection<string> runIds);
}

/// <summary>
/// The whole diff of a step, as a text file: one line for each file, "mark TAB size TAB path", where the mark is "-" for
/// what is deleted or removed, "+" for what is new, copied or restored, "~" for what is changed and "=" for what is kept.
/// A backslash, a line break or a tab in a path is written as \\, \n or \t, so that every file stays one line.
/// </summary>
public sealed partial class PlanDiffStore(string directory) : IPlanDiffStore
{
    [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
    private static partial Regex IdPattern();

    private string PathOf(string runId, string stepId) =>
        IdPattern().IsMatch(runId) && IdPattern().IsMatch(stepId)
            ? Path.Combine(directory, runId, stepId + ".diff.txt")
            : throw new ArgumentException("The id is not valid.");

    public void Write(string runId, string stepId, string header, IEnumerable<PlanLine> lines)
    {
        string path = PathOf(runId, stepId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (StreamWriter writer = new(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)) { NewLine = "\n" })
            {
                foreach (string line in header.Split('\n'))
                {
                    writer.WriteLine("# " + line.TrimEnd('\r'));
                }

                writer.WriteLine("# mark<TAB>bytes<TAB>path   (- deleted, + new or copied, ~ changed, = kept)");
                foreach (PlanLine line in lines)
                {
                    writer.Write(Mark(line.Action));
                    writer.Write('\t');
                    writer.Write(line.Size);
                    writer.Write('\t');
                    writer.WriteLine(Escape(line.Path));
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Stream? Open(string runId, string stepId)
    {
        string path = PathOf(runId, stepId);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void Keep(IReadOnlyCollection<string> runIds)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string folder in Directory.EnumerateDirectories(directory))
        {
            if (runIds.Contains(Path.GetFileName(folder)))
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // It is only a copy of what the plan showed; it is tried again next time.
            }
        }
    }

    private static char Mark(string action) => action switch
    {
        "delete" or "remove" => '-',
        "modified" => '~',
        "keep" => '=',
        _ => '+',
    };

    internal static string Escape(string path) =>
        path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
}
