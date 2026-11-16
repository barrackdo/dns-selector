using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DnsSelector.Models;

namespace DnsSelector.Services;

public static class ThemeManager
{
    private const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(AppTheme theme)
    {
        var dict = theme switch
        {
            AppTheme.Dark => new ResourceDictionary { Source = new Uri("Themes/Dark.xaml", UriKind.Relative) },
            _ => new ResourceDictionary { Source = new Uri("Themes/Light.xaml", UriKind.Relative) },
        };

        var app = Application.Current;
        if (app is null) return;

        // 既存のテーマ辞書を置き換える
        for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var source = app.Resources.MergedDictionaries[i].Source;
            if (source is not null && source.OriginalString.StartsWith("Themes/"))
                app.Resources.MergedDictionaries.RemoveAt(i);
        }
        app.Resources.MergedDictionaries.Insert(0, dict);

        // 開いている全ウィンドウのタイトルバー色を更新
        foreach (Window window in app.Windows)
            ApplyTitleBar(window, theme == AppTheme.Dark);
    }

    public static void ApplyTitleBar(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // COLORREF は BGR 順
            var color = dark ? 0x1E1E1E : 0xFFFFFF;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
        }
        catch
        {
            // 未対応 OS では無視
        }
    }

    public static AppTheme GetSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            // レジストリ読み込み失敗時はライト
        }
        return AppTheme.Light;
    }

    public static AppTheme Resolve(AppTheme requested)
        => requested == AppTheme.System ? GetSystemTheme() : requested;
}