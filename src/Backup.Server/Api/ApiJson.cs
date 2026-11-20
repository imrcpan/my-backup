using System.Text.Json.Serialization;

namespace Backup.Server.Api;

public sealed record Pong(string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pong))]
internal sealed partial class ApiJson : JsonSerializerContext;
