using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AutoChartSwitch.Core;
using AutoChartSwitch.GameBridge;

namespace AutoChartSwitch.Tests;

public sealed class RelayDiscoveryTests
{
    [Theory]
    [InlineData("\"1.0\"", "\"1.0\"", "\"Selection\"")]
    [InlineData("1.0", "1.0", "0")]
    [InlineData("1", "1", "\"LobbySelection\"")]
    public async Task RelayFramesAcceptBothGameMakerNumberAndEnumEncodings(string protocol, string sequence, string kind)
    {
        var root = CreateRoot();
        var sharedPath = Path.Combine(root, "shared.json");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await WriteDiscoveryAsync(sharedPath, ((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            await using var source = new RelayGameEventSource(root, sharedPath);
            var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.EventReceived += (_, envelope) => received.TrySetResult(envelope);
            await source.StartAsync();
            using var subscriber = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var payload = Encoding.UTF8.GetBytes($"{{\"protocolVersion\":{protocol},\"sequence\":{sequence},\"kind\":{kind}}}\0");
            var frame = new byte[4 + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
            payload.CopyTo(frame, 4);
            await subscriber.GetStream().WriteAsync(frame);
            var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, envelope.ProtocolVersion);
            Assert.Equal(1, envelope.Sequence);
            Assert.Equal(kind.Contains("LobbySelection", StringComparison.Ordinal) ? GameEventKind.LobbySelection : GameEventKind.Selection, envelope.Kind);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RefusedLocalEndpointFallsBackToSharedDiscovery()
    {
        var root = CreateRoot();
        using var stale = new TcpListener(IPAddress.Loopback, 0);
        stale.Start();
        var stalePort = ((IPEndPoint)stale.LocalEndpoint).Port;
        stale.Stop();
        using var live = new TcpListener(IPAddress.Loopback, 0);
        live.Start();
        var livePort = ((IPEndPoint)live.LocalEndpoint).Port;
        var sharedPath = Path.Combine(root, "shared.json");
        await WriteDiscoveryAsync(Path.Combine(root, "AutoChartSwitchV2", "bridge-relay.json"), stalePort);
        await WriteDiscoveryAsync(sharedPath, livePort);
        try
        {
            await using var source = new RelayGameEventSource(root, sharedPath);
            var connected = ObserveConnection(source);
            await source.StartAsync();
            using var subscriber = await live.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(livePort, source.Port);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FindRelayWakesRetryAfterSeveralAutomaticWaits()
    {
        var root = CreateRoot();
        var sharedPath = Path.Combine(root, "shared.json");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await using var source = new RelayGameEventSource(root, sharedPath);
            var retries = 0;
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.StatusChanged += (_, status) =>
            {
                if (status.StartsWith("API relay not found", StringComparison.Ordinal) &&
                    Interlocked.Increment(ref retries) == 3) waiting.TrySetResult();
            };
            var connected = ObserveConnection(source);
            await source.StartAsync();
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WriteDiscoveryAsync(sharedPath, port);
            await source.FindRelayAsync();
            using var subscriber = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(750));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(port, source.Port);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StoppedSourceCanStartAndBeWokenAgain()
    {
        var root = CreateRoot();
        var sharedPath = Path.Combine(root, "shared.json");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            await using var source = new RelayGameEventSource(root, sharedPath);
            await source.StartAsync();
            await source.StopAsync();
            await source.StartAsync();
            var connected = ObserveConnection(source);
            await WriteDiscoveryAsync(sharedPath, ((IPEndPoint)listener.LocalEndpoint).Port);
            await source.FindRelayAsync();
            using var subscriber = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(750));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-relay-discovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "AutoChartSwitchV2"));
        return root;
    }

    private static Task WriteDiscoveryAsync(string path, int port) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            protocolVersion = 1,
            host = "127.0.0.1",
            gamePort = 28745,
            subscriberPort = port,
            processId = int.MaxValue
        }));

    private static TaskCompletionSource ObserveConnection(RelayGameEventSource source)
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StatusChanged += (_, status) =>
        {
            if (status.StartsWith("Relay subscriber connected", StringComparison.Ordinal)) connected.TrySetResult();
        };
        return connected;
    }
}
