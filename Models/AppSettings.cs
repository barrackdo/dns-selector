using System.Text.Json.Serialization;

namespace DnsSelector.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    [JsonPropertyName("theme")]
    public AppTheme Theme { get; set; } = AppTheme.System;

    [JsonPropertyName("lastAdapterName")]
    public string LastAdapterName { get; set; } = "";

    [JsonPropertyName("speedTestSamples")]
    public int SpeedTestSamples { get; set; } = 5;
}