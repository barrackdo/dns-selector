using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsSelector.Models;

namespace DnsSelector.Services;

public sealed class DnsService
{
    private readonly IPowerShellRunner _powerShell;

    public DnsService(IPowerShellRunner powerShell)
    {
        _powerShell = powerShell;
    }

    /// <summary>Up 状態のネットワークアダプターを取得します（物理・接続済み優先で並べ替え）。</summary>
    public async Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default)
    {
        const string script =
            "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | " +
            "ForEach-Object { \"$($_.Name)`t$($_.InterfaceIndex)`t$($_.InterfaceDescription)`t$($_.MediaConnectionState)\" }";

        var output = await _powerShell.RunAsync(script, ct).ConfigureAwait(false);
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t', 4);
            if (parts.Length < 2 || !uint.TryParse(parts[1], out var index))
                continue;

            var name = parts[0];
            var description = parts.Length > 2 ? parts[2] : "";
            var mediaState = parts.Length > 3 ? parts[3] : "";

            var isVirtual = IsVirtualAdapter(description);
            var isConnected = mediaState == "Connected";

            adapters.Add(new NetworkAdapterInfo
            {
                Name = name,
                InterfaceIndex = index,
                Description = description,
                IsVirtual = isVirtual,
                IsConnected = isConnected,
            });
        }

        // ソート: 物理(非仮想)優先 → 接続済み優先 → 名前順
        adapters.Sort((a, b) =>
        {
            var v = a.IsVirtual.CompareTo(b.IsVirtual);
            if (v != 0) return v;
            var c = b.IsConnected.CompareTo(a.IsConnected);
            if (c != 0) return c;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return adapters;
    }

    private static bool IsVirtualAdapter(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;
        var lower = description.ToLowerInvariant();
        // Hyper-V, WSL, Virtual, TAP, WireGuard, VPN など
        var virtualKeywords = new[] { "hyper-v", "virtual", "tap", "wireguard", "vpn", "loopback", "ws", "veth" };
        return virtualKeywords.Any(k => lower.Contains(k));
    }

    /// <summary>指定アダプターの現在の DNS サーバーを取得します。</summary>
    public async Task<IReadOnlyList<string>> GetCurrentDnsAsync(uint interfaceIndex, CancellationToken ct = default)
    {
        var script =
            $"Get-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ErrorAction SilentlyContinue | " +
            "ForEach-Object { \"$($_.AddressFamily)`t$($_.ServerAddresses -join ', ')\" }";

        var output = await _powerShell.RunAsync(script, ct).ConfigureAwait(false);
        var result = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t', 2);
            if (parts.Length != 2 || parts[1].Length == 0)
                continue;

            var family = parts[0] == "2" ? "IPv4" : parts[0] == "23" ? "IPv6" : parts[0];
            result.Add($"{family}: {parts[1]}");
        }

        return result;
    }

    /// <summary>指定アダプターの DNS が DHCP（自動取得）かどうかを取得します。</summary>
    /// <remarks>
    /// IP レベルの DHCP（Get-NetIPInterface の Dhcp）は DNS の取得元とは異なるため、
    /// Set-DnsClientServerAddress が書き込むレジストリの NameServer 値で判定します。
    /// NameServer が空なら DHCP による自動取得とみなします。
    /// </remarks>
    public async Task<bool> IsDhcpAsync(uint interfaceIndex, CancellationToken ct = default)
    {
        var script =
            $"$guid = (Get-NetAdapter -InterfaceIndex {interfaceIndex} -ErrorAction SilentlyContinue).InterfaceGuid; " +
            "if (-not $guid) { 'unknown'; return }; " +
            "$v4 = (Get-ItemProperty \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\$guid\" -Name NameServer -ErrorAction SilentlyContinue).NameServer; " +
            "$v6 = (Get-ItemProperty \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters\\Interfaces\\$guid\" -Name NameServer -ErrorAction SilentlyContinue).NameServer; " +
            "if ([string]::IsNullOrWhiteSpace($v4) -and [string]::IsNullOrWhiteSpace($v6)) { 'auto' } else { 'static' }";

        var output = await _powerShell.RunAsync(script, ct).ConfigureAwait(false);
        return output.Trim().ToLowerInvariant() == "auto";
    }

    /// <summary>プロファイルの DNS を適用します。</summary>
    public async Task ApplyAsync(uint interfaceIndex, DnsProfile profile, CancellationToken ct = default)
    {
        var script = new List<string>
        {
            $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ResetServerAddresses"
        };

        if (profile.Ipv4.Count > 0)
            script.Add($"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses @({FormatAddresses(profile.Ipv4)})");

        if (profile.Ipv6.Count > 0)
            script.Add($"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses @({FormatAddresses(profile.Ipv6)})");

        await _powerShell.RunAsync(string.Join("; ", script), ct).ConfigureAwait(false);
    }

    /// <summary>DHCP による自動取得に戻します。</summary>
    public async Task ResetAsync(uint interfaceIndex, CancellationToken ct = default)
    {
        var script = $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ResetServerAddresses";
        await _powerShell.RunAsync(script, ct).ConfigureAwait(false);
    }

    private static string FormatAddresses(IEnumerable<string> addresses)
        => string.Join(", ", addresses.Select(a => "'" + a.Replace("'", "''") + "'"));
}