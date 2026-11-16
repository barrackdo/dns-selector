namespace DnsSelector.Models;

public sealed class NetworkAdapterInfo
{
    public string Name { get; init; } = "";
    public uint InterfaceIndex { get; init; }
    public string Description { get; init; } = "";
    public bool IsVirtual { get; init; }
    public bool IsConnected { get; init; }

    public string DisplayName => $"{Name} ({Description})";
    public string GroupName => IsVirtual ? "仮想アダプター" : "物理アダプター";
}