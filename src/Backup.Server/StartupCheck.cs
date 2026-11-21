using Backup.App.Execution;
using Backup.Core;
using Backup.Infra.Persistence;

namespace Backup.Server;

/// <summary>
/// Runs when the host starts, before the server listens. It holds the data directory for this instance and reads the
/// saved state and the history of runs, so that what can stop the application from starting is found at the start
/// and said in a sentence, and a run that was going when the program stopped is marked as interrupted at once.
/// </summary>
public sealed class StartupCheck(IConfiguration configuration, IServiceProvider services) : IHostedLifecycleService, IDisposable
{
    private DataDirectoryLock? _lock;

    /// <exception cref="DomainException">"data_dir_in_use" or "state_corrupt".</exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _lock = DataDirectoryLock.Acquire(AppSetup.DataDirectory(configuration));
        services.GetRequiredService<Workspace>();
        services.GetRequiredService<RunService>();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _lock?.Dispose();
}
