using Backup.App.Browsing;
using Backup.App.Execution;
using Backup.App.Planning;
using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Disks;
using Backup.Infra.Logging;
using Backup.Infra.Persistence;
using Backup.Infra.Process;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.Server;

public static class AppSetup
{
    /// <summary>Where the application keeps its state and logs: DataDir, or ~/.local/share/backup-manager.</summary>
    public static string DataDirectory(IConfiguration configuration)
    {
        string? configured = configuration["DataDir"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        string home = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(home, "backup-manager");
    }

    public static IServiceCollection AddBackupServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IDiskProbe>(_ => new DiskProbe());
        services.AddSingleton<IDeviceResolver>(sp => new DeviceResolver(sp.GetRequiredService<IDiskProbe>()));
        services.AddSingleton<IResticClient>(sp =>
        {
            IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
            return new ResticClient(
                sp.GetRequiredService<IProcessRunner>(),
                new ResticOptions(configuration["ResticPath"] ?? "restic", configuration["RcloneConfig"]));
        });
        services.AddSingleton<IRcloneClient>(sp =>
        {
            IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
            return new RcloneClient(
                sp.GetRequiredService<IProcessRunner>(),
                new RcloneOptions(configuration["RclonePath"] ?? "rclone", configuration["RcloneConfig"]));
        });
        services.AddSingleton(sp => new Workspace(new JsonStateStore(Path.Combine(DataDirectory(sp.GetRequiredService<IConfiguration>()), "state.json"))));
        services.AddSingleton(sp => new RunCatalog(
            new JsonRunStore(Path.Combine(DataDirectory(sp.GetRequiredService<IConfiguration>()), "runs.json")),
            sp.GetRequiredService<TimeProvider>().GetUtcNow()));
        services.AddSingleton<IRunLog>(sp => new RunLog(Path.Combine(DataDirectory(sp.GetRequiredService<IConfiguration>()), "logs")));
        services.AddSingleton<RunPlanner>();
        services.AddSingleton<RunService>();
        services.AddSingleton<RepositoryReader>();
        services.AddSingleton<ObjectBrowser>();
        return services;
    }
}
