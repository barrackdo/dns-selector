using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using MaxMind.GeoIP2.Responses;

namespace DnsSelector.Services;

public sealed class LocationService : IDisposable
{
    private const string MmdbFileName = "GeoIP.mmdb";
    private const string EmbeddedResourceName = "DnsSelector.Resources.GeoData.nginx-geo.txt";

    private readonly DatabaseReader? _mmdbReader;
    private readonly Lazy<List<CidrEntryV4>>? _fetusEntries;

    public LocationService()
    {
        // DnsSelector.exe と同じディレクトリに GeoIP.mmdb がある場合はそちらを優先
        var exeDir = AppContext.BaseDirectory;
        var mmdbPath = Path.Combine(exeDir, MmdbFileName);

        if (File.Exists(mmdbPath))
        {
            try
            {
                _mmdbReader = new DatabaseReader(mmdbPath);
            }
            catch
            {
                _mmdbReader = null;
            }
        }

        // fetus データは最初の使用時にだけロード（遅延読み込み）
        _fetusEntries = new Lazy<List<CidrEntryV4>>(LoadEmbeddedFetusData);
    }

    public Task<string?> GetLocationAsync(string ip, CancellationToken ct = default)
        => Task.FromResult(GetLocation(ip));

    public string? GetLocation(string ip)
    {
        // 1. MMDB（MaxMind DB）
        if (_mmdbReader is not null)
        {
            try
            {
                var loc = GetLocationFromMmdb(ip);
                if (loc is not null)
                    return loc;
            }
            catch
            {
                // MMDB の解決に失敗した場合は次の手段へ
            }
        }

        // 2. 組み込み ipv4.fetus.jp データ（IPv4 のみ）
        var fetusEntries = _fetusEntries?.Value;
        if (fetusEntries is not null && fetusEntries.Count > 0)
        {
            try
            {
                var loc = GetLocationFromFetus(ip);
                if (loc is not null)
                    return loc;
            }
            catch
            {
            }
        }

        return null;
    }

    private string? GetLocationFromMmdb(string ip)
    {
        var addr = IPAddress.Parse(ip);

        // まず City メソッドで試みる（都市情報付き DB の場合）
        try
        {
            var city = _mmdbReader!.City(addr);
            return FormatFromCity(city);
        }
        catch (GeoIP2Exception)
        {
            // Country-only DB の場合 City メソッドは失敗する → Country メソッドで試す
            try
            {
                var country = _mmdbReader!.Country(addr);
                return FormatFromCountry(country);
            }
            catch
            {
                throw; // 上位へ伝播してフォールバック
            }
        }
    }

    private static string? FormatFromCity(CityResponse city)
    {
        var countryCode = city.Country.IsoCode;
        var countryName =
            CountryCodes.GetCountryName(countryCode ?? string.Empty)
            ?? city.Country.Name;

        var cityName = city.City.Name;
        var regionName = city.MostSpecificSubdivision?.Name;

        if (!string.IsNullOrWhiteSpace(cityName) && !string.IsNullOrWhiteSpace(countryName))
            return $"{cityName}, {countryName}";
        if (!string.IsNullOrWhiteSpace(regionName) && !string.IsNullOrWhiteSpace(countryName))
            return $"{regionName}, {countryName}";
        if (!string.IsNullOrWhiteSpace(countryName))
            return countryName;
        return countryCode;
    }

    private static string? FormatFromCountry(CountryResponse country)
    {
        var countryCode = country.Country.IsoCode;
        var countryName =
            CountryCodes.GetCountryName(countryCode ?? string.Empty)
            ?? country.Country.Name;

        if (!string.IsNullOrWhiteSpace(countryName))
            return countryName;
        return countryCode;
    }

    private string? GetLocationFromFetus(string ip)
    {
        var entries = _fetusEntries?.Value;
        if (entries is null || entries.Count == 0)
            return null;

        if (!IPAddress.TryParse(ip, out var addr))
            return null;

        // ipv4.fetus.jp は IPv4 のみサポート
        if (addr.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var ipValue = ToUInt32(addr);

        foreach (var entry in entries)
        {
            if ((ipValue & entry.Mask) == entry.Network)
            {
                var countryName = CountryCodes.GetCountryName(entry.CountryCode);
                return countryName ?? entry.CountryCode;
            }
        }

        return null;
    }

    private List<CidrEntryV4> LoadEmbeddedFetusData()
    {
        var entries = new List<CidrEntryV4>(180_000);

        var assembly = typeof(LocationService).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return entries;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            // コメント行・空行・nginx ディレクティブ行をスキップ
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;
            if (trimmed is "{" or "}")
                continue;
            if (trimmed.StartsWith("geo ", StringComparison.Ordinal))
                continue;

            // "default XX;" はスキップ
            if (trimmed.StartsWith("default ", StringComparison.Ordinal))
                continue;

            // "1.0.16.0/20      JP;" → CIDR と国コードを取り出す
            var withoutSem = trimmed.TrimEnd(';');
            var parts = withoutSem.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var cidr = parts[0];
            var countryCode = parts[1];

            if (TryParseCidrV4(cidr, out var network, out var mask))
                entries.Add(new CidrEntryV4(network, mask, countryCode));
        }

        // ネットワークアドレス順に並べることで、探索を早期終了できるようにする
        entries.Sort(static (a, b) => a.Network.CompareTo(b.Network));

        return entries;
    }

    private static bool TryParseCidrV4(string cidr, out uint network, out uint mask)
    {
        network = 0;
        mask = 0;

        var slash = cidr.IndexOf('/');
        if (slash < 0)
            return false;

        if (!IPAddress.TryParse(cidr[..slash], out var addr))
            return false;
        if (addr.AddressFamily != AddressFamily.InterNetwork)
            return false;
        if (!int.TryParse(cidr[(slash + 1)..], out var prefixLength))
            return false;
        if (prefixLength is < 0 or > 32)
            return false;

        var ipValue = ToUInt32(addr);
        mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        network = ipValue & mask;
        return true;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    public void Dispose()
    {
        _mmdbReader?.Dispose();
        // fetus データは Lazy のみで、Dispose しない
        GC.SuppressFinalize(this);
    }

    private readonly record struct CidrEntryV4(uint Network, uint Mask, string CountryCode);
}