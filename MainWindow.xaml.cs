using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DnsSelector.Models;
using DnsSelector.Services;

namespace DnsSelector;

public partial class MainWindow : Window
{
    private readonly ProfileStore _profileStore = new();
    private readonly DnsService _dnsService = new(new PowerShellRunner());
    private readonly SettingsService _settings = new();
    private readonly SpeedTestService _speedTest = new();
    private readonly LocationService _location = new();

    private ObservableCollection<NetworkAdapterInfo> _adapters = new();
    private CollectionViewSource _adapterView;
    private bool _isLoadingAdapters;
    private bool _busy;
    private int _speedTestElapsedSec;

    private NetworkAdapterInfo? _selectedAdapter;
    private DnsProfile? _selectedProfile;
    private readonly DispatcherTimer _statusTimer = new();
    private readonly DispatcherTimer _speedTestTimer = new();

    public MainWindow()
    {
        InitializeComponent();

        _statusTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = "";
        };

        _speedTestTimer.Interval = TimeSpan.FromSeconds(1);
        _speedTestTimer.Tick += (_, _) =>
        {
            _speedTestElapsedSec++;
            SetStatus($"速度テスト実施中... ({_speedTestElapsedSec} sec.)", isError: false, temporary: false);
        };

        ProfileGrid.ItemsSource = _profileStore.Profiles;

        _adapterView = new CollectionViewSource { Source = _adapters };
        _adapterView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NetworkAdapterInfo.GroupName)));
        AdapterComboBox.ItemsSource = _adapterView.View;

        Loaded += async (_, _) => await OnLoadedAsync();
        Deactivated += (_, _) =>
        {
            var theme = ThemeManager.Resolve(_settings.Settings.Theme);
            ThemeManager.ApplyTitleBar(this, theme == AppTheme.Dark);
        };
    }

    private async Task OnLoadedAsync()
    {
        _settings.TryLoad(out _);

        var theme = ThemeManager.Resolve(_settings.Settings.Theme);
        ThemeManager.Apply(theme);
        ThemeManager.ApplyTitleBar(this, theme == AppTheme.Dark);

        // プロファイルを読み込む
        _profileStore.TryLoad(out var loadError, out var loadedProfiles);
        foreach (var profile in loadedProfiles)
            _profileStore.Profiles.Add(profile);

        if (loadError.Length > 0)
            Log($"Profile load warning: {loadError}");

        _isLoadingAdapters = true;
        try
        {
            var adapters = await _dnsService.GetAdaptersAsync();
            _adapters.Clear();
            foreach (var adapter in adapters)
                _adapters.Add(adapter);

            if (!string.IsNullOrWhiteSpace(_settings.Settings.LastAdapterName))
            {
                var saved = _adapters.FirstOrDefault(a => a.Name == _settings.Settings.LastAdapterName);
                if (saved is not null)
                {
                    AdapterComboBox.SelectedItem = saved;
                    _selectedAdapter = saved;
                }
                else if (_adapters.Count > 0)
                {
                    AdapterComboBox.SelectedItem = _adapters[0];
                    _selectedAdapter = _adapters[0];
                }
            }
            else if (_adapters.Count > 0)
            {
                AdapterComboBox.SelectedItem = _adapters[0];
                _selectedAdapter = _adapters[0];
            }

            if (_profileStore.Profiles.Count > 0)
                ProfileGrid.SelectedItem = _profileStore.Profiles[0];
        }
        catch (Exception ex)
        {
            Log($"OnLoaded failed: {ex}");
            SetStatus($"初期化に失敗しました: {ex.Message}", isError: true);
        }
        finally
        {
            _isLoadingAdapters = false;
        }

        await RefreshCurrentDnsAsync();
    }

    private void SetUiEnabled(bool enabled)
    {
        _busy = !enabled;
        ApplyButton.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
        SpeedTestButton.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        SettingsButton.IsEnabled = enabled;
        ProfileGrid.IsEnabled = enabled;
        AdapterComboBox.IsEnabled = enabled;
    }

    private void AdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingAdapters)
            return;

        if (AdapterComboBox.SelectedItem is NetworkAdapterInfo adapter)
        {
            _selectedAdapter = adapter;
            _settings.Settings.LastAdapterName = adapter.Name;
            _settings.Save();
            _ = RefreshCurrentDnsAsync();
        }
    }

    private void ProfileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is DnsProfile profile)
        {
            _selectedProfile = profile;
            ShowDetailPanel(profile);
        }
        else
        {
            HideDetailPanel();
        }
    }

    private void ShowDetailPanel(DnsProfile profile)
    {
        _selectedProfile = profile;
        DetailPanel.DataContext = profile;
        DetailPanel.Visibility = Visibility.Visible;

        _ = Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(profile.Location))
            {
                var ip = profile.Ipv4.FirstOrDefault() ?? profile.Ipv6.FirstOrDefault();
                if (ip is not null)
                {
                    var loc = await _location.GetLocationAsync(ip);
                    if (loc is not null)
                    {
                        try
                        {
                            var existing = _profileStore.Profiles.FirstOrDefault(p => p.Name == profile.Name);
                            if (existing is not null)
                            {
                                existing.SetLocation(loc);
                                _profileStore.Save(_profileStore.Profiles);
                            }
                        }
                        catch { }
                    }
                }
            }
        });
    }

    private void HideDetailPanel()
    {
        _selectedProfile = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailPanel.DataContext = null;
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch { }
    }

    private async Task RefreshCurrentDnsAsync(NetworkAdapterInfo? adapter = null)
    {
        adapter ??= _selectedAdapter;
        if (adapter is null)
        {
            CurrentDnsTitle.Text = "現在のDNS設定";
            CurrentDnsText.Text = "アダプターが選択されていません。";
            SetStatus("");
            return;
        }

        var msg = "現在のDNS設定を読み込んでいます...";
        SetStatus(msg);
        var servers = await _dnsService.GetCurrentDnsAsync(adapter.InterfaceIndex);
        var isDhcp = await _dnsService.IsDhcpAsync(adapter.InterfaceIndex);

        string providerName;
        if (isDhcp)
        {
            providerName = "自動";
        }
        else
        {
            providerName = ResolveProviderName(servers);
        }

        CurrentDnsTitle.Text = $"現在のDNS設定 ({providerName})";
        CurrentDnsText.Text = servers.Count > 0
            ? string.Join(Environment.NewLine, servers)
            : "（DHCP による自動取得）";

        await Task.Delay(1000);
        await Dispatcher.InvokeAsync(() => SetStatus(""));
    }

    private string ResolveProviderName(IReadOnlyList<string> servers)
    {
        if (servers.Count == 0)
            return "自動";

        // サーバー文字列（例: "IPv4: 1.1.1.1, 1.0.0.1"）をファミリ別に IP アドレスを抽出
        var currentIpv4 = new List<string>();
        var currentIpv6 = new List<string>();

        foreach (var server in servers)
        {
            var s = server.Trim();
            var sepIdx = s.IndexOf(": ", StringComparison.Ordinal);
            if (sepIdx < 0)
                continue;

            var family = s.Substring(0, sepIdx).Trim();
            var addressesPart = s.Substring(sepIdx + 2);
            foreach (var ip in addressesPart.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = ip.Trim();
                if (string.IsNullOrEmpty(t))
                    continue;

                if (family == "IPv4")
                    currentIpv4.Add(t);
                else if (family == "IPv6")
                    currentIpv6.Add(t);
            }
        }

        foreach (var profile in _profileStore.Profiles)
        {
            // 両方のファミリについて、現在設定されているアドレスがプロファイルのアドレスと一致するか確認
            var ipv4Match = currentIpv4.Count == 0 || currentIpv4.All(ip => profile.Ipv4.Contains(ip));
            var ipv6Match = currentIpv6.Count == 0 || currentIpv6.All(ip => profile.Ipv6.Contains(ip));

            if (ipv4Match && ipv6Match && (currentIpv4.Count > 0 || currentIpv6.Count > 0))
                return profile.Name;
        }

        return "不明";
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (_selectedAdapter is not { } adapter)
        {
            SetStatus("アダプターが選択されていません。", isError: true);
            return;
        }

        if (_selectedProfile is not { } profile)
        {
            SetStatus("プロファイルを選択してください。", isError: true);
            return;
        }

        if (!profile.IsValid())
        {
            SetStatus($"プロファイル「{profile.Name}」は無効です。", isError: true);
            return;
        }

        _busy = true;
        SetStatus($"プロファイル「{profile.Name}」を適用しています...");
        try
        {
            await _dnsService.ApplyAsync(adapter.InterfaceIndex, profile);
            SetStatus($"プロファイル「{profile.Name}」を適用しました。", isError: false);
            await RefreshCurrentDnsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"DNS の適用に失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"適用に失敗しました: {ex.Message}", isError: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (_selectedAdapter is not { } adapter)
        {
            SetStatus("アダプターが選択されていません。", isError: true);
            return;
        }

        _busy = true;
        SetStatus($"プロバイダー設定を DHCP に戻します...");
        try
        {
            await _dnsService.ResetAsync(adapter.InterfaceIndex);
            SetStatus("DHCP による自動取得に戻しました。", isError: false);
            await RefreshCurrentDnsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"DHCP への復帰に失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"復帰に失敗しました: {ex.Message}", isError: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void SpeedTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is not { } profile)
            return;

        if (profile.Ipv4.Count == 0 && profile.Ipv6.Count == 0)
        {
            SetStatus("計測する DNS サーバーがありません。", isError: true);
            return;
        }

        _speedTestElapsedSec = 0;
        SetUiEnabled(false);
        _speedTestTimer.Start();

        try
        {
            var servers = new List<string>();
            if (profile.Ipv4.Count > 0)
                servers.AddRange(profile.Ipv4);
            if (profile.Ipv6.Count > 0)
                servers.AddRange(profile.Ipv6);

            var totalAvg = 0.0;
            var count = 0;
            foreach (var srv in servers)
            {
                var res = await _speedTest.MeasureAsync(srv, CancellationToken.None);
                if (!double.IsNaN(res.AverageMs))
                {
                    totalAvg += res.AverageMs;
                    count++;
                    SetStatus($"サーバー {srv} の計測完了", isError: false, temporary: true);
                }
            }

            if (count > 0)
            {
                totalAvg /= count;
                var testedAt = DateTimeOffset.UtcNow;
                profile.SetLatency(totalAvg, testedAt);

                var firstIp = servers.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstIp))
                {
                    var loc = await _location.GetLocationAsync(firstIp, CancellationToken.None);
                    if (loc is not null)
                        profile.SetLocation(loc);
                }

                _profileStore.Save(_profileStore.Profiles);

                // DataGrid を強制リフレッシュ
                ProfileGrid.Items.Refresh();

                SetStatus($"平均レイテンシ: {totalAvg:F1} ms", isError: false, temporary: true);
            }
            else
            {
                SetStatus("すべてのサーバーでタイムアウトしました", isError: true, temporary: true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"速度テストに失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("エラー", isError: true, temporary: true);
        }
        finally
        {
            _speedTestTimer.Stop();
            SetUiEnabled(true);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _isLoadingAdapters = true;
        try
        {
            var adapters = await _dnsService.GetAdaptersAsync();
            _adapters.Clear();
            foreach (var adapter in adapters)
                _adapters.Add(adapter);

            if (_adapters.Count > 0)
            {
                AdapterComboBox.SelectedItem = _selectedAdapter is not null
                    ? _adapters.FirstOrDefault(a => a.Name == _selectedAdapter.Name) ?? _adapters[0]
                    : _adapters[0];
                _selectedAdapter = AdapterComboBox.SelectedItem as NetworkAdapterInfo;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"アダプターの取得に失敗しました: {ex.Message}", isError: true);
        }
        finally
        {
            _isLoadingAdapters = false;
        }

        _ = RefreshCurrentDnsAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    private void SetStatus(string text, bool isError = false, bool temporary = true)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? Brushes.Red : Brushes.White;
        if (temporary)
        {
            _statusTimer.Stop();
            _statusTimer.Start();
        }
        else
        {
            _statusTimer.Stop();
        }
    }

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "dns-selector-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}