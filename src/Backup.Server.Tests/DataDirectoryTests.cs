using Microsoft.Extensions.Configuration;
using Xunit;

namespace Backup.Server.Tests;

[Collection("environment")]
public sealed class DataDirectoryTests : IDisposable
{
    private readonly string? _xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

    public void Dispose() => Environment.SetEnvironmentVariable("XDG_DATA_HOME", _xdg);

    private static IConfiguration Config(string? dataDir) =>
        new ConfigurationBuilder().AddInMemoryCollection(dataDir == null ? [] : new Dictionary<string, string?> { ["DataDir"] = dataDir }).Build();

    [Fact]
    public void A_configured_directory_wins_and_is_made_absolute()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/xdg");
        Assert.Equal("/somewhere/else", AppSetup.DataDirectory(Config("/somewhere/else")));
        Assert.Equal(Path.GetFullPath("relative/dir"), AppSetup.DataDirectory(Config("relative/dir")));
    }

    [Fact]
    public void Without_a_setting_the_data_home_of_the_user_is_used()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/xdg");
        Assert.Equal("/xdg/backup-manager", AppSetup.DataDirectory(Config(null)));
        Assert.Equal("/xdg/backup-manager", AppSetup.DataDirectory(Config("  ")));
    }

    [Fact]
    public void Without_either_it_is_below_the_home_directory()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        string directory = AppSetup.DataDirectory(Config(null));
        Assert.EndsWith("/.local/share/backup-manager", directory);
        Assert.StartsWith("/", directory);
    }
}

[CollectionDefinition("environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;
