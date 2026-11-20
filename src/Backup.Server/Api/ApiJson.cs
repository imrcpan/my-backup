using System.Text.Json.Serialization;
using Backup.Core;
using Backup.Infra.Restic;

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
[JsonSerializable(typeof(GroupDto))]
[JsonSerializable(typeof(GroupDto[]))]
[JsonSerializable(typeof(ObjectDto))]
[JsonSerializable(typeof(NameRequest))]
[JsonSerializable(typeof(ObjectRequest))]
[JsonSerializable(typeof(RetentionRequest))]
[JsonSerializable(typeof(SnapshotDto[]))]
[JsonSerializable(typeof(ResticNode[]))]
[JsonSerializable(typeof(SystemDto))]
internal sealed partial class ApiJson : JsonSerializerContext;
