namespace Backup.Infra.Restic;

/// <param name="RcloneConfigFile">Given to restic as RCLONE_CONFIG, so that "rclone:" repositories use it.</param>
public sealed record ResticOptions(string ExecutablePath = "restic", string? RcloneConfigFile = null);
