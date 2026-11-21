using System.Text.Json;
using System.Text.Json.Serialization;
using Backup.Core;

namespace Backup.Infra.Persistence;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppState))]
internal sealed partial class AppStateJson : JsonSerializerContext;

/// <summary>Keeps the state in one JSON file.</summary>
public sealed class JsonStateStore(string path) : IStateStore
{
    public AppState Load()
    {
        if (!File.Exists(path))
        {
            return new AppState();
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppStateJson.Default.AppState)
                ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException)
        {
            // Not overwritten: the file may still hold passwords that the user needs.
            string copy = File.Exists(BackupPath) ? $" A copy of the version before is in {BackupPath}." : "";
            throw new DomainException("state_corrupt", $"The state file {path} cannot be read. It was left as it is.{copy}");
        }
    }

    private string BackupPath => path + ".bak";

    /// <summary>
    /// Writes the state. The file holds the passwords of the repositories, and without them the backups cannot be opened, so
    /// the version that was there is kept as a copy first, unless it cannot be read, which would replace a good copy with a bad one.
    /// </summary>
    public void Save(AppState state)
    {
        if (File.Exists(path))
        {
            try
            {
                string previous = File.ReadAllText(path);
                if (JsonSerializer.Deserialize(previous, AppStateJson.Default.AppState) != null)
                {
                    AtomicFile.WriteAllText(BackupPath, previous);
                }
            }
            catch (JsonException)
            {
                // A file that cannot be read is not kept over a copy that can.
            }
        }

        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(state, AppStateJson.Default.AppState));
    }
}
