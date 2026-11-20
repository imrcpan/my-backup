namespace Backup.Core;

/// <summary>
/// A rule of the domain was broken. <see cref="Code"/> is a stable
/// machine-readable key that the user interface turns into a message.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
