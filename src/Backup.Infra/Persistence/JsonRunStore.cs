using System.Text.Json;
using System.Text.Json.Serialization;
using Backup.Core;
using Backup.Core.Runs;

namespace Backup.Infra.Persistence;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<Run>))]
internal sealed partial class RunsJson : JsonSerializerContext;

public sealed class JsonRunStore(string path) : IRunStore
{
    public List<Run> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), RunsJson.Default.ListRun)
                ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException)
        {
            throw new DomainException("state_corrupt", $"The run history {path} cannot be read. It was left as it is.");
        }
    }

    public void Save(IReadOnlyList<Run> runs) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize([.. runs], RunsJson.Default.ListRun));
}
