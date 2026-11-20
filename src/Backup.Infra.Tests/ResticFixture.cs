using Backup.Infra.Process;
using Backup.Infra.Restic;

namespace Backup.Infra.Tests;

/// <summary>A scratch directory with a source tree and a restic repository in it; the real restic runs on it.</summary>
public sealed class ResticFixture : IDisposable
{
    public const string Password = "test-password-9f3";

    public ResticFixture()
    {
        Root = Directory.CreateTempSubdirectory("restic-test").FullName;
        Source = Directory.CreateDirectory(Path.Combine(Root, "source")).FullName;
        Repository = new ResticRepository(Path.Combine(Root, "repo"), Password);
        Client = new ResticClient(new ProcessRunner());
    }

    public string Root { get; }

    public string Source { get; }

    public ResticRepository Repository { get; }

    public ResticClient Client { get; }

    public string Write(string relative, string content)
    {
        string path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, true);
}
