using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DnsSelector.Models;

public sealed class DnsProfile : INotifyPropertyChanged
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("website")]
    public string Website { get; set; } = "";

    [JsonPropertyName("ipv4")]
    public List<string> Ipv4 { get; set; } = new();

    [JsonPropertyName("ipv6")]
    public List<string> Ipv6 { get; set; } = new();

    [JsonPropertyName("latencyMs")]
    public double? LatencyMs { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("testedAt")]
    public DateTimeOffset? TestedAt { get; set; }

    /// <summary>
    /// ローカルタイムゾーンに変換した測定時刻を表示用に返します。
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? TestedAtLocal => TestedAt?.ToLocalTime();

    [JsonPropertyName("features")]
    public string Features { get; set; } = "";

    public bool IsValid()
        => Name.Trim().Length > 0 &&
           (Ipv4.Count > 0 || Ipv6.Count > 0) &&
           Ipv4.All(IsIpv4) &&
           Ipv6.All(IsIpv6);

    private static bool IsIpv4(string s)
        => System.Net.IPAddress.TryParse(s, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private static bool IsIpv6(string s)
        => System.Net.IPAddress.TryParse(s, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new(name));

    public void SetLatency(double? ms, DateTimeOffset? testedAt)
    {
        LatencyMs = ms;
        TestedAt = testedAt;
        Notify(nameof(LatencyMs));
        Notify(nameof(TestedAt));
    }

    public void SetLocation(string? loc)
    {
        Location = loc;
        Notify(nameof(Location));
    }
}

public sealed class ProviderFile
{
    [JsonPropertyName("profiles")]
    public List<DnsProfile> Profiles { get; set; } = new();
}