using Backup.Core.Runs;
using Backup.Infra.Persistence;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class PlanDiffStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("diff-test").FullName, "plans");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, true);

    private static PlanLine Line(string action, string path, long size = 0) => new() { Action = action, Path = path, Size = size };

    private static string ReadAll(Stream? stream)
    {
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Every_file_is_a_line_with_its_mark_its_size_and_its_path_and_the_order_is_kept()
    {
        PlanDiffStore store = new(_dir);
        store.Write("run1", "step1", "Sync to Disk", [Line("delete", "/old.txt", 7), Line("copy", "/a.txt", 5), Line("modified", "/b.txt", 9), Line("keep", "/c", 1), Line("new", "/d")]);

        string[] lines = ReadAll(store.Open("run1", "step1")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("# Sync to Disk", lines[0]);
        Assert.Equal(["-\t7\t/old.txt", "+\t5\t/a.txt", "~\t9\t/b.txt", "=\t1\t/c", "+\t0\t/d"], lines.Where(l => !l.StartsWith('#')));
    }

    [Fact]
    public void A_path_with_a_line_break_a_tab_or_a_backslash_stays_on_one_line()
    {
        PlanDiffStore store = new(_dir);
        store.Write("run1", "step1", "x", [Line("delete", "/we\nird\tna\\me\r.txt")]);

        string text = ReadAll(store.Open("run1", "step1"));

        string line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single(l => !l.StartsWith('#'));
        Assert.Equal("-\t0\t/we\\nird\\tna\\\\me\\r.txt", line);
    }

    [Fact]
    public void A_diff_of_many_lines_is_written_whole_and_a_second_write_replaces_it_without_leaving_files()
    {
        PlanDiffStore store = new(_dir);
        store.Write("run1", "step1", "h", Enumerable.Range(0, 100_000).Select(i => Line("copy", $"/f{i}", i)));
        Assert.Equal(100_000, ReadAll(store.Open("run1", "step1")).Split('\n').Count(l => l.StartsWith("+\t")));

        store.Write("run1", "step1", "h", [Line("delete", "/only")]);

        Assert.Single(ReadAll(store.Open("run1", "step1")).Split('\n'), l => l.StartsWith("-\t"));
        Assert.Equal(["step1.diff.txt"], Directory.GetFiles(Path.Combine(_dir, "run1")).Select(Path.GetFileName));
    }

    [Fact]
    public void A_step_without_a_file_gives_nothing_and_an_id_cannot_leave_the_folder()
    {
        PlanDiffStore store = new(_dir);
        Assert.Null(store.Open("run1", "nothing"));
        Assert.Throws<ArgumentException>(() => store.Open("../escape", "step1"));
        Assert.Throws<ArgumentException>(() => store.Write("run1", "a/b", "h", []));
        Assert.Throws<ArgumentException>(() => store.Open("", "step1"));
    }

    [Fact]
    public void Keep_removes_the_files_of_runs_that_are_gone_and_leaves_the_others()
    {
        PlanDiffStore store = new(_dir);
        store.Write("keepme", "s1", "h", [Line("copy", "/a")]);
        store.Write("gone1", "s1", "h", [Line("copy", "/a")]);
        store.Write("gone2", "s2", "h", [Line("copy", "/a")]);

        store.Keep(["keepme"]);

        Assert.NotNull(store.Open("keepme", "s1"));
        Assert.Null(store.Open("gone1", "s1"));
        Assert.Null(store.Open("gone2", "s2"));
        new PlanDiffStore(Path.Combine(_dir, "none")).Keep([]);
    }
}
