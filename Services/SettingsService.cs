using System;
using System.IO;
using System.Text.Json;
using DnsSelector.Models;

namespace DnsSelector.Services;

public sealed class SettingsService
{
    private const string FileName = "dns-selector-settings.json";
    public string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    public AppSettings Settings { get; private set; } = new();

    public bool TryLoad(out string error)
    {
        error = "";
        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, options);
                if (loaded is not null)
                {
                    Settings = loaded;
                    return true;
                }
                error = "設定ファイルの形式が正しくありません。既定値を使用します。";
            }
            catch (Exception ex)
            {
                error = $"設定ファイルの読み込みに失敗しました: {ex.Message}";
            }
        }

        Settings = new AppSettings();
        Save();
        return false;
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, options));
    }
}