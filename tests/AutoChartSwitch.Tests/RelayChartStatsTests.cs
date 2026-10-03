using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AutoChartSwitch.Core;
using AutoChartSwitch.GameBridge;

namespace AutoChartSwitch.Tests;

public sealed class RelayChartStatsTests
{
    [Theory]
    [InlineData("ChartInfo", false)]
    [InlineData("Selection", true)]
    [InlineData("LobbySelection", false)]
    [InlineData("ChartLoadingStarted", true)]
    [InlineData("ChartStarted", false)]
    public async Task RelayPreservesAllSixChartStats(string kind, bool numericStrings)
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-stats", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var discovery = Path.Combine(root, "bridge-relay.json");
        await File.WriteAllTextAsync(discovery, JsonSerializer.Serialize(new
        {
            protocolVersion = 1, gamePort = 28745, host = "127.0.0.1",
            subscriberPort = ((IPEndPoint)listener.LocalEndpoint).Port
        }));
        try
        {
            await using var source = new RelayGameEventSource(root, discovery);
            var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.EventReceived += (_, envelope) => received.TrySetResult(envelope);
            await source.StartAsync();
            using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var values = new[] { "12.5", "34", "56.75", "78", "90", "6" };
            var keys = new[] { "chip", "tech", "stream", "chord", "burst", "gimmick" };
            var stats = string.Join(",", keys.Zip(values, (key, value) => $"\"{key}\":{(numericStrings ? $"\"{value}\"" : value)}"));
            var payload = Encoding.UTF8.GetBytes(
                $"{{\"protocolVersion\":\"1.0\",\"sequence\":1.0,\"kind\":\"{kind}\",\"chart\":{{\"chartId\":\"stats\",\"techStats\":{{{stats}}}}}}}\0");
            var frame = new byte[4 + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
            payload.CopyTo(frame, 4);
            await client.GetStream().WriteAsync(frame);
            var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(Enum.Parse<GameEventKind>(kind), envelope.Kind);
            Assert.Equal(new decimal[] { 12.5m, 34m, 56.75m, 78m, 90m, 6m }, envelope.Chart!.ToChartInfo().TechStats.Values);
        }
        finally { Directory.Delete(root, true); }
    }
}
