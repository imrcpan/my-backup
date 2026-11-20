using System.Text.Json.Serialization;
using Backup.Core;

namespace Backup.Server.Api;

public sealed record Pong(string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pong))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(DeviceDto))]
[JsonSerializable(typeof(DeviceDto[]))]
[JsonSerializable(typeof(DeviceRequest))]
[JsonSerializable(typeof(DeviceStatusDto))]
[JsonSerializable(typeof(DirectoryDto[]))]
internal sealed partial class ApiJson : JsonSerializerContext;
