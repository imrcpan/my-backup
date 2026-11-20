using System.Security.Cryptography;
using System.Text;
using Backup.Core;

namespace Backup.App.Planning;

public static class Fingerprint
{
    /// <summary>
    /// A hash of everything a plan depends on in the configuration: the objects and their devices, with
    /// the passwords only as part of the hash. A plan whose fingerprint differs now is stale.
    /// </summary>
    public static string Of(IEnumerable<StorageObject> objects, IReadOnlyDictionary<string, StorageDevice> devices)
    {
        StringBuilder text = new();
        foreach (StorageObject item in objects)
        {
            devices.TryGetValue(item.DeviceId, out StorageDevice? device);
            text.Append(item.Id).Append('|').Append(item.DeviceId).Append('|').Append(item.Path).Append('|')
                .Append(item.Encrypted).Append('|').Append(item.Password).Append('|')
                .Append(device?.Kind).Append('|').Append(device?.Uuid).Append('|').Append(device?.RcloneRemote).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32];
    }
}
