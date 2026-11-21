namespace Backup.Core.Runs;

public interface IRunStore
{
    List<Run> Load();

    void Save(IReadOnlyList<Run> runs);
}

/// <summary>The runs, kept across restarts. A run that was going when the application stopped is marked so.</summary>
public sealed class RunCatalog
{
    public const int KeepFinished = 200;

    private readonly object _gate = new();
    private readonly IRunStore _store;
    private readonly List<Run> _runs;

    public RunCatalog(IRunStore store, DateTimeOffset now)
    {
        _store = store;
        _runs = store.Load();
        bool changed = false;
        foreach (Run run in _runs)
        {
            if (run.State == RunState.Running)
            {
                run.State = RunState.Interrupted;
                run.FinishedAt = now;
                run.ErrorMessage = "The application stopped while this run was going.";
                foreach (RunStep step in run.Steps)
                {
                    if (step.State == StepState.Running)
                    {
                        step.State = StepState.Interrupted;
                    }
                    else if (step.State == StepState.Pending)
                    {
                        step.State = StepState.Skipped;
                    }
                }

                changed = true;
            }
            else if (run.State == RunState.Planned && now - run.CreatedAt > Run.PlanLifetime)
            {
                run.State = RunState.Discarded;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>Copies of the runs, newest first. A run goes on changing while it is read and sent out, so what leaves the lock is a copy.</summary>
    public IReadOnlyList<Run> List()
    {
        lock (_gate)
        {
            return [.. _runs.OrderByDescending(r => r.CreatedAt).Select(r => r.Clone())];
        }
    }

    /// <summary>A copy of the run; changes to it are not changes to the run (use <see cref="Update"/>).</summary>
    public Run? Find(string id)
    {
        lock (_gate)
        {
            return _runs.Find(r => r.Id == id)?.Clone();
        }
    }

    public Run? Active()
    {
        lock (_gate)
        {
            return _runs.Find(r => r.State == RunState.Running)?.Clone();
        }
    }

    public void Add(Run run)
    {
        lock (_gate)
        {
            _runs.Add(run);
            Trim();
            Save();
        }
    }

    /// <summary>Runs <paramref name="change"/> on the run under the lock and saves.</summary>
    public void Update(string id, Action<Run> change)
    {
        lock (_gate)
        {
            Run run = _runs.Find(r => r.Id == id) ?? throw new DomainException("run_not_found", "The run does not exist.");
            change(run);
            Save();
        }
    }

    /// <summary>Changes the run under the lock without saving: for progress, which is not worth a write each time.</summary>
    public void Touch(string id, Action<Run> change)
    {
        lock (_gate)
        {
            Run? run = _runs.Find(r => r.Id == id);
            if (run != null)
            {
                change(run);
            }
        }
    }

    public T Read<T>(string id, Func<Run, T> read)
    {
        lock (_gate)
        {
            return read(_runs.Find(r => r.Id == id) ?? throw new DomainException("run_not_found", "The run does not exist."));
        }
    }

    private void Trim()
    {
        List<Run> finished = [.. _runs.Where(r => r.IsFinished).OrderByDescending(r => r.CreatedAt)];
        foreach (Run old in finished.Skip(KeepFinished))
        {
            _runs.Remove(old);
        }
    }

    private void Save() => _store.Save(_runs);
}
