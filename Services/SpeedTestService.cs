using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DnsSelector.Services;

public sealed class SpeedTestResult
{
    public double AverageMs { get; set; }
    public List<double> Samples { get; set; } = new();
}

public sealed class SpeedTestService
{
    private const int TimeoutMs = 2000;
    private const int SampleCount = 3;
    private const int FallbackSampleCount = 2;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public Task<SpeedTestResult> MeasureAsync(string server, CancellationToken ct = default)
        => Task.Run(() => MeasureSync(server, ct), ct);

    private static SpeedTestResult MeasureSync(string server, CancellationToken ct)
    {
        // まず DoH (HTTPS) を試す - 既知のプロバイダーでは最速で確実
        var dohSamples = TryMeasureDoh(server, ct, SampleCount);
        if (dohSamples.Count > 0)
            return new SpeedTestResult { AverageMs = dohSamples.Average(), Samples = dohSamples };

        // TCP で試す（ファイアウォールを通りやすい）
        var tcpSamples = TryMeasureTcp(server, ct, FallbackSampleCount);
        if (tcpSamples.Count > 0)
            return new SpeedTestResult { AverageMs = tcpSamples.Average(), Samples = tcpSamples };

        // UDP で試す（従来方式）
        var udpSamples = TryMeasureUdp(server, ct, FallbackSampleCount);
        if (udpSamples.Count > 0)
            return new SpeedTestResult { AverageMs = udpSamples.Average(), Samples = udpSamples };

        return new SpeedTestResult { AverageMs = double.NaN };
    }

    private static List<double> TryMeasureTcp(string server, CancellationToken ct, int sampleCount)
    {
        var samples = new List<double>();
        try
        {
            var addr = IPAddress.Parse(server);
            var family = addr.AddressFamily == AddressFamily.InterNetworkV6
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            using var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            socket.SendTimeout = TimeoutMs;
            socket.ReceiveTimeout = TimeoutMs;
            socket.Connect(new IPEndPoint(addr, 53));

            for (var i = 0; i < sampleCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var payload = BuildQueryWithLength();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                socket.Send(payload, payload.Length, SocketFlags.None);
                var buffer = new byte[512];
                var received = socket.Receive(buffer);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        catch { }
        return samples;
    }

    private static List<double> TryMeasureUdp(string server, CancellationToken ct, int sampleCount)
    {
        var samples = new List<double>();
        try
        {
            var addr = IPAddress.Parse(server);
            var family = addr.AddressFamily == AddressFamily.InterNetworkV6
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            using var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = TimeoutMs;
            socket.Connect(new IPEndPoint(addr, 53));

            for (var i = 0; i < sampleCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var payload = BuildQuery();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                socket.Send(payload, payload.Length, SocketFlags.None);
                var buffer = new byte[512];
                var received = socket.Receive(buffer);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        catch { }
        return samples;
    }

    private static List<double> TryMeasureDoh(string server, CancellationToken ct, int sampleCount)
    {
        var samples = new List<double>();
        try
        {
            // Cloudflare DoH エンドポイントを使用してテスト
            var dohUrl = $"https://{server}/dns-query?name=example.com&type=A";
            // 注意: すべての DNS サーバーが DoH を提供しているわけではない
            // ここでは主要なプロバイダーの DoH エンドポイントをハードコード
            var endpoint = server switch
            {
                "1.1.1.1" or "1.0.0.1" or "1.1.1.3" or "1.0.0.3" => "https://cloudflare-dns.com/dns-query?name=example.com&type=A",
                "8.8.8.8" or "8.8.4.4" => "https://dns.google/resolve?name=example.com&type=A",
                "9.9.9.9" or "149.112.112.112" or "9.9.9.10" or "149.112.112.10" => "https://dns.quad9.net/dns-query?name=example.com&type=A",
                "94.140.14.14" or "94.140.15.15" or "94.140.14.15" or "94.140.15.16" => "https://dns.adguard-dns.com/dns-query?name=example.com&type=A",
                "208.67.222.222" or "208.67.220.220" => "https://doh.opendns.com/dns-query?name=example.com&type=A",
                _ => null
            };

            if (endpoint == null) return samples;

            for (var i = 0; i < sampleCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Add("Accept", "application/dns-json");
                var response = _httpClient.Send(request, ct);
                response.EnsureSuccessStatusCode();
                var json = response.Content.ReadAsStringAsync(ct).Result;
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        catch { }
        return samples;
    }

    private static byte[] BuildQuery()
    {
        var hostname = $"dns-{Guid.NewGuid():N}.example.com";
        var labels = hostname.Split('.');
        var query = new List<byte>
        {
            0x12, 0x34, // ID
            0x01, 0x00, // Flags: standard query
            0, 1, // QDCOUNT = 1
            0, 0, 0, 0, // ANCOUNT, NSCOUNT, ARCOUNT = 0
        };

        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            query.Add((byte)bytes.Length);
            foreach (var b in bytes)
                query.Add(b);
        }
        query.Add(0); // QNAME terminator

        query.Add(0); query.Add(1); // QTYPE = A
        query.Add(0); query.Add(1); // QCLASS = IN
        return query.ToArray();
    }

    // TCP用: 2バイトの長さプレフィックスが必要
    private static byte[] BuildQueryWithLength()
    {
        var query = BuildQuery();
        var result = new List<byte>
        {
            (byte)(query.Length >> 8),
            (byte)(query.Length & 0xFF)
        };
        result.AddRange(query);
        return result.ToArray();
    }
}