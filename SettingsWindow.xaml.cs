using System;
using System.Linq;
using System.Windows;
using DnsSelector.Models;
using DnsSelector.Services;

namespace DnsSelector;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;

    public SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;

        var current = settings.Settings;
        if (current.Theme == AppTheme.System)
        {
            SystemThemeCheckBox.IsChecked = true;
            DarkModeToggle.IsChecked = ThemeManager.GetSystemTheme() == AppTheme.Dark;
            DarkModeToggle.IsEnabled = false;
        }
        else
        {
            SystemThemeCheckBox.IsChecked = false;
            DarkModeToggle.IsChecked = current.Theme == AppTheme.Dark;
            DarkModeToggle.IsEnabled = true;
        }

        SamplesTextBox.Text = current.SpeedTestSamples.ToString();

        DarkModeToggle.Checked += DarkModeToggle_Checked;
        DarkModeToggle.Unchecked += DarkModeToggle_Unchecked;

        Loaded += (_, _) =>
        {
            var theme = ThemeManager.Resolve(_settings.Settings.Theme);
            ThemeManager.ApplyTitleBar(this, theme == AppTheme.Dark);
        };
        Deactivated += (_, _) =>
        {
            var theme = ThemeManager.Resolve(_settings.Settings.Theme);
            ThemeManager.ApplyTitleBar(this, theme == AppTheme.Dark);
        };
    }

    private void SystemThemeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var useSystem = SystemThemeCheckBox.IsChecked == true;
        DarkModeToggle.IsEnabled = !useSystem;
        if (useSystem)
            DarkModeToggle.IsChecked = ThemeManager.GetSystemTheme() == AppTheme.Dark;
    }

    private void DarkModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (SystemThemeCheckBox.IsChecked == true)
            return;

        _settings.Settings.Theme = AppTheme.Dark;
        ThemeManager.Apply(AppTheme.Dark);
        ThemeManager.ApplyTitleBar(this, true);
    }

    private void DarkModeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (SystemThemeCheckBox.IsChecked == true)
            return;

        _settings.Settings.Theme = AppTheme.Light;
        ThemeManager.Apply(AppTheme.Light);
        ThemeManager.ApplyTitleBar(this, false);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var theme = SystemThemeCheckBox.IsChecked == true
            ? AppTheme.System
            : (DarkModeToggle.IsChecked == true ? AppTheme.Dark : AppTheme.Light);

        _settings.Settings.Theme = theme;

        if (int.TryParse(SamplesTextBox.Text, out var samples) && samples > 0)
            _settings.Settings.SpeedTestSamples = samples;

        _settings.Save();
        ThemeManager.Apply(ThemeManager.Resolve(theme));
        ThemeManager.ApplyTitleBar(this, theme == AppTheme.Dark);

        DialogResult = true;
        Close();
    }
}