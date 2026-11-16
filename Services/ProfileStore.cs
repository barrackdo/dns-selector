using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using DnsSelector.Models;

namespace DnsSelector.Services;

public sealed class ProfileStore
{
    private const string ResourceName = "DnsSelector.Resources.default-providers.json";
    private const string FileName = "dns-providers.json";

    public string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    public ObservableCollection<DnsProfile> Profiles { get; } = new();

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// 設定ファイルを読み込みます。ファイルが無ければ既定値を書き出し、それを使用します。
    /// </summary>
    public bool TryLoad(out string error, out List<DnsProfile> profiles)
    {
        error = "";

        if (File.Exists(FilePath))
        {
            try
            {
                profiles = ParseProfiles(File.ReadAllText(FilePath));
                if (profiles.Count > 0)
                {
                    IsLoaded = true;
                    return true;
                }

                error = "設定ファイルに有効なプロファイルがありません。既定値を使用します。";
            }
            catch (Exception ex)
            {
                error = $"設定ファイルの読み込みに失敗しました: {ex.Message}";
            }
        }

        profiles = ParseProfiles(ReadEmbeddedDefaults());
        try
        {
            Save(profiles);
        }
        catch
        {
            // 既定値の書き出しに失敗しても読み込み自体は継続する
        }

        IsLoaded = profiles.Count > 0;
        return IsLoaded;
    }

    public void Save(IReadOnlyList<DnsProfile> profiles)
    {
        var doc = new ProviderFile
        {
            Profiles = new List<DnsProfile>(profiles),
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(doc, options));
    }

    private static string ReadEmbeddedDefaults()
    {
        using var stream = typeof(ProfileStore).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("既定のプロファイル定義が見つかりません。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<DnsProfile> ParseProfiles(string json)
    {
        var result = new List<DnsProfile>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("profiles", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var element in array.EnumerateArray())
        {
            var profile = new DnsProfile
            {
                Name = ReadString(element, "name"),
                Description = ReadString(element, "description"),
                Website = ReadString(element, "website"),
                Ipv4 = ReadStringArray(element, "ipv4"),
                Ipv6 = ReadStringArray(element, "ipv6"),
                LatencyMs = ReadDouble(element, "latencyMs"),
                Location = ReadStringOrNull(element, "location"),
                TestedAt = ReadDateTimeOffset(element, "testedAt"),
            };

            if (profile.Name.Length > 0)
                result.Add(profile);
        }

        return result;
    }

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? ReadStringOrNull(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadDouble(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var dt)
            ? dt
            : null;

    private static List<string> ReadStringArray(JsonElement element, string property)
    {
        var list = new List<string>();
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s.Trim());
            }
        }

        return list;
    }
}