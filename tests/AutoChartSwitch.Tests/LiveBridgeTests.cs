using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AutoChartSwitch.Core;
using AutoChartSwitch.GameBridge;

namespace AutoChartSwitch.Tests;

public sealed class LiveBridgeTests
{
    [Theory]
    [InlineData("OPENING", "OPN")]
    [InlineData("middle", "MID")]
    [InlineData("FINALE", "FIN")]
    [InlineData("ENCORE", "ENC")]
    [InlineData("BACKSTAGE", "BKS")]
    [InlineData("custom difficulty", "SHATTER")]
    [InlineData("", "SHATTER")]
    public void DifficultyNamesNormalizeToCanonicalCodes(string input, string expected) =>
        Assert.Equal(expected, DifficultyCodes.Normalize(input));

    [Fact]
    public void SnapshotCodeAlsoNormalizesUnknownValuesToShatter()
    {
        var snapshot = new GameChartSnapshot { RawDifficultyName = "custom", DifficultyCode = "custom" };
        Assert.Equal("SHATTER", snapshot.CanonicalDifficultyCode);
        Assert.Equal("SHATTER", snapshot.ToChartInfo().DifficultyName);
    }

    [Fact]
    public void AssetResolverUsesCanonicalDifficultyAndGameCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-v2-assets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "song_chart-SHATTER.png");
            File.WriteAllText(path, "asset");
            var chart = new ChartInfo { GameChartId = "chart", DifficultyName = "SHATTER" };
            Assert.Equal(Path.GetFullPath(path), ChartAssetResolver.ResolveJacket(chart, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AssetResolverUsesMemoriesSacrificeFallbackWhenChartJacketIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-v2-fallback", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fallback = Path.Combine(root, "Memories_Sacrifice_jacket.png");
            File.WriteAllText(fallback, "fallback");
            var chart = new ChartInfo { GameChartId = "missing", DifficultyName = "MID" };

            Assert.Equal(Path.GetFullPath(fallback), ChartAssetResolver.ResolveJacket(chart, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AssetResolverUsesFallbackWhenNoJacketOrChartIdIsProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-v2-empty-jacket", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fallback = Path.Combine(root, "Memories_Sacrifice_jacket.png");
            File.WriteAllText(fallback, "fallback");

            var resolved = ChartAssetResolver.ResolveJacket(new ChartInfo(), root);

            Assert.Equal(Path.GetFullPath(fallback), resolved);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AssetResolverCopiesStagedGameJacketIntoConfiguredOutputFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-v2-staged-jacket", Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(root, "game", "AutoChartSwitchV2", "Jackets");
        var output = Path.Combine(root, "configured-output");
        Directory.CreateDirectory(staging);
        try
        {
            var source = Path.Combine(staging, "chart-OPENING.png");
            File.WriteAllText(source, "native-jacket");
            var chart = new ChartInfo
            {
                GameChartId = "chart",
                DifficultyName = "OPN",
                JacketPath = source
            };

            var resolved = ChartAssetResolver.ResolveJacket(chart, output);

            Assert.Equal(Path.Combine(output, "chart-OPN.png"), resolved);
            Assert.Equal("native-jacket", File.ReadAllText(resolved));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TcpSourceAcceptsLengthPrefixedEventAndDeduplicatesSequence()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new List<GameEventEnvelope>();
        source.EventReceived += (_, value) => received.Add(value);
        await source.StartAsync();

        var envelope = new GameEventEnvelope
        {
            Sequence = 3,
            Kind = GameEventKind.Selection,
            Chart = new GameChartSnapshot { ChartId = "song", RawDifficultyName = "OPENING" }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            var frame = new byte[sizeof(int) + json.Length];
            BitConverter.GetBytes(json.Length).CopyTo(frame, 0);
            json.CopyTo(frame, sizeof(int));
            await stream.WriteAsync(frame);
            await stream.FlushAsync();
            await Task.Delay(100);
            await stream.WriteAsync(frame);
            await stream.FlushAsync();
        }

        Assert.Single(received);
        Assert.Equal("OPN", received[0].Chart!.CanonicalDifficultyCode);
        await source.StopAsync();
    }

    [Fact]
    public async Task RelaySourceConnectsUsingDiscoveryWithoutDependingOnRelayProcessId()
    {
        var root = Path.Combine(Path.GetTempPath(), "acs-v2-relay", Guid.NewGuid().ToString("N"));
        var discoveryDirectory = Path.Combine(root, "AutoChartSwitchV2");
        Directory.CreateDirectory(discoveryDirectory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var subscriberPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        await File.WriteAllTextAsync(
            Path.Combine(discoveryDirectory, "bridge-relay.json"),
            JsonSerializer.Serialize(new
            {
                protocolVersion = 1,
                host = "127.0.0.1",
                gamePort = 28745,
                subscriberPort,
                processId = int.MaxValue
            }));

        try
        {
            await using var source = new RelayGameEventSource(root);
            var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.EventReceived += (_, envelope) => received.TrySetResult(envelope);
            await source.StartAsync();

            using var subscriber = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var envelope = new GameEventEnvelope
            {
                Sequence = 1,
                Kind = GameEventKind.Selection,
                Chart = new GameChartSnapshot { ChartId = "relay-song", RawDifficultyName = "OPENING" }
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var frame = new byte[sizeof(int) + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
            payload.CopyTo(frame, sizeof(int));
            await subscriber.GetStream().WriteAsync(frame);

            var receivedEnvelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("relay-song", receivedEnvelope.Chart!.ChartId);
            Assert.Equal(subscriberPort, source.Port);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task TcpSourceAcceptsSequenceRestartAfterGameReconnect()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new List<GameEventEnvelope>();
        var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) =>
        {
            lock (received)
            {
                received.Add(value);
                if (received.Count == 1) firstReceived.TrySetResult();
                if (received.Count == 2) bothReceived.TrySetResult();
            }
        };
        await source.StartAsync();

        await SendEventAsync(port, 3);
        await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await SendEventAsync(port, 1);
        await bothReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        lock (received)
        {
            Assert.Equal([3, 1], received.Select(x => x.Sequence));
        }
    }

    [Fact]
    public async Task TcpSourceAcceptsGameMakerRealIntegersAndStringEventKind()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            {
              "protocolVersion": 1.0,
              "sequence": 7.0,
              "kind": "Selection",
              "chart": {
                "chartId": "game-maker-song",
                "rawDifficultyName": "OPENING",
                "title": "\u66f2\u540d",
                "formattedTitle": "TITLE",
                "difficultyNumber": 12.5,
                "techStats": { "chip": 1.0, "tech": 2.0, "stream": 3.0, "chord": 4.0, "burst": 5.0, "gimmick": 0.0 }
              }
            }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, envelope.ProtocolVersion);
        Assert.Equal(7, envelope.Sequence);
        Assert.Equal(GameEventKind.Selection, envelope.Kind);
        Assert.Equal(12.5m, envelope.Chart!.DifficultyNumber);
        Assert.Equal("\u66f2\u540d", envelope.Chart.Title);
        Assert.Equal("TITLE", envelope.Chart.FormattedTitle);
        Assert.Equal(5m, envelope.Chart.TechStats.Burst);
    }

    [Fact]
    public async Task TcpSourcePreservesBoundaryShatterMetadataAndTechStats()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            {
              "protocolVersion": 1.0,
              "sequence": 9.0,
              "kind": "LobbySelection",
              "chart": {
                "chartId": "SVCB3S",
                "rawDifficultyName": "VIOLENCE",
                "difficultyCode": "VIOLENCE",
                "charter": "What for Keyboard(grode.)",
                "difficultyNumber": 16.0,
                "techStats": { "chip": 12.0, "tech": 34.0, "stream": 56.0, "chord": 78.0, "burst": 90.0, "gimmick": 1.0 }
              }
            }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameEventKind.LobbySelection, envelope.Kind);
        Assert.Equal("VIOLENCE", envelope.Chart!.RawDifficultyName);
        Assert.Equal("SHATTER", envelope.Chart.ToChartInfo().DifficultyName);
        Assert.Equal("What for Keyboard(grode.)", envelope.Chart.Charter);
        Assert.Equal(16m, envelope.Chart.DifficultyNumber);
        Assert.Equal(34m, envelope.Chart.TechStats.Tech);
    }

    [Fact]
    public async Task TcpSourceAcceptsChartLoadingStartedEventKind()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            {
              "protocolVersion": 1.0,
              "sequence": 1.0,
              "kind": "ChartLoadingStarted",
              "chart": null
            }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameEventKind.ChartLoadingStarted, envelope.Kind);
    }

    [Fact]
    public async Task TcpSourceAcceptsLobbySelectionEventKind()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            {
              "protocolVersion": 1.0,
              "sequence": 1.0,
              "kind": "LobbySelection",
              "chart": { "chartId": "lobby-song", "rawDifficultyName": "FINALE" }
            }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameEventKind.LobbySelection, envelope.Kind);
        Assert.Equal("lobby-song", envelope.Chart!.ChartId);
    }

    [Fact]
    public async Task TcpSourceAcceptsWorldcrossRoomTelemetryAndIgnoresItInChartFlow()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            {
              "protocolVersion": "1.0",
              "sequence": "12.0",
              "kind": "WorldcrossRoom",
              "worldcross": { "players": [
                { "steamId64": "76561198000000001", "name": "Alice", "state": "ready", "rating": "12.5", "class": 7, "score": 1010000, "lastPlayScore": "1010000", "label": "FC" },
                { "steamId64": "76561198000000002", "name": "Bot", "state": "unready", "rating": 0, "class": 0, "score": 0, "lastPlayScore": 0, "label": "" }
              ] }
            }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameEventKind.WorldcrossRoom, envelope.Kind);
        Assert.Equal("76561198000000001", envelope.Worldcross!.Players[0].SteamId64);
        Assert.Equal(1010000m, envelope.Worldcross.Players[0].Score);
        Assert.Equal(1010000m, envelope.Worldcross.Players[0].LastPlayScore);
        Assert.Equal("FC", envelope.Worldcross.Players[0].Label);
        Assert.Null(envelope.Chart);
    }

    [Fact]
    public async Task TcpSourceAcceptsNumericWorldcrossGameplayEventKind()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            { "protocolVersion": 1.0, "sequence": 13.0, "kind": 7,
              "worldcross": { "players": [ { "steamId64": "76561198000000003", "state": "playing", "score": "1234.0", "label": "VS" } ] } }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameEventKind.WorldcrossGameplay, envelope.Kind);
        Assert.Equal(1234m, envelope.Worldcross!.Players[0].Score);
        Assert.Equal("VS", envelope.Worldcross.Players[0].Label);
    }

    [Fact]
    public async Task TcpSourceAcceptsChartExitTransitionStartedEventKind()
    {
        var port = GetFreePort();
        await using var source = new TcpGameEventSource(port);
        var received = new TaskCompletionSource<GameEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.EventReceived += (_, value) => received.TrySetResult(value);
        await source.StartAsync();

        const string json = """
            {
              "protocolVersion": 1.0,
              "sequence": 1.0,
              "kind": "ChartExitTransitionStarted",
              "chart": null
            }
            """;

        await SendRawEventAsync(port, json);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameEventKind.ChartExitTransitionStarted, envelope.Kind);
    }

    private static async Task SendEventAsync(int port, long sequence)
    {
        var envelope = new GameEventEnvelope
        {
            Sequence = sequence,
            Kind = GameEventKind.Selection,
            Chart = new GameChartSnapshot { ChartId = $"song-{sequence}", RawDifficultyName = "OPENING" }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var frame = new byte[sizeof(int) + json.Length];
        BitConverter.GetBytes(json.Length).CopyTo(frame, 0);
        json.CopyTo(frame, sizeof(int));
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    private static async Task SendRawEventAsync(int port, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var frame = new byte[sizeof(int) + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
        payload.CopyTo(frame, sizeof(int));
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
