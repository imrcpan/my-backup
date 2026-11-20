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
            throw new DomainException("state_corrupt", $"The state file {path} cannot be read. It was left as it is.");
        }
    }

    public void Save(AppState state) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(state, AppStateJson.Default.AppState));
}
