using System.Text.Json.Serialization;

namespace Backup.Infra.Restic;

/// <summary>Which snapshots a forget keeps and which it removes.</summary>
public sealed record ResticForgetPlan(IReadOnlyList<ResticSnapshot> Keep, IReadOnlyList<ResticSnapshot> Remove);

internal sealed record ResticForgetGroup(ResticSnapshot[]? Keep, ResticSnapshot[]? Remove);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ResticForgetGroup[]))]
internal sealed partial class ResticForgetJson : JsonSerializerContext;
