namespace NimbusLedger.Core.Models;

public enum DeviceMatchKind { Missing, StableId, NameOnly, Ambiguous }

public sealed record DeviceIdentity(string Key, Guid[] StableIds, string[] Names)
{
    public static DeviceIdentity From(ComputerAccount device) =>
        new(device.ObjectGuid.ToString(), device.AzureAdDeviceId.HasValue
            ? [device.ObjectGuid, device.AzureAdDeviceId.Value] : [device.ObjectGuid],
            [device.SamAccountName, device.DnsHostName ?? string.Empty]);

    public static DeviceIdentity From(DeviceRecord device) =>
        new(device.Id.ToString(), device.AzureAdDeviceId.HasValue
            ? [device.AzureAdDeviceId.Value] : [], [device.DisplayName]);

    public static DeviceIdentity From(ConfigMgrDevice device) =>
        new(device.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            device.ActiveDirectoryObjectGuid.HasValue ? [device.ActiveDirectoryObjectGuid.Value] : [], [device.Name]);
}

public sealed class DeviceIdentityIndex
{
    private readonly Dictionary<Guid, HashSet<string>> _ids = [];
    private readonly Dictionary<string, HashSet<string>> _names = new(StringComparer.OrdinalIgnoreCase);

    public DeviceIdentityIndex(IEnumerable<DeviceIdentity> devices)
    {
        foreach (DeviceIdentity device in devices)
        {
            foreach (Guid id in device.StableIds.Where(id => id != Guid.Empty))
            {
                if (!_ids.TryGetValue(id, out HashSet<string>? keys))
                    _ids[id] = keys = new(StringComparer.Ordinal);
                keys.Add(device.Key);
            }
            foreach (string name in device.Names.Select(NormalizeName).Where(name => name.Length > 0))
            {
                if (!_names.TryGetValue(name, out HashSet<string>? keys))
                    _names[name] = keys = new(StringComparer.Ordinal);
                keys.Add(device.Key);
            }
        }
    }

    public DeviceMatchKind Match(DeviceIdentity device)
    {
        HashSet<string> matches = [];
        foreach (Guid id in device.StableIds)
        {
            if (_ids.TryGetValue(id, out HashSet<string>? keys))
                matches.UnionWith(keys);
        }
        if (matches.Count > 0)
            return matches.Count == 1 ? DeviceMatchKind.StableId : DeviceMatchKind.Ambiguous;

        foreach (string name in device.Names.Select(NormalizeName))
        {
            if (_names.TryGetValue(name, out HashSet<string>? keys))
                matches.UnionWith(keys);
        }
        return matches.Count switch
        {
            0 => DeviceMatchKind.Missing,
            1 => DeviceMatchKind.NameOnly,
            _ => DeviceMatchKind.Ambiguous
        };
    }

    public static string NormalizeName(string value) =>
        value.Trim().TrimEnd('$').Split('.', 2)[0];
}
