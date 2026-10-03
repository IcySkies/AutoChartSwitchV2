using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoChartSwitch.Core;

namespace AutoChartSwitch.GameBridge;

public sealed class RelayGameEventSource : IGameEventSource
{
    private const int MaxFrameBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _gamePath;
    private readonly string _sharedDiscoveryPath;
    private TcpClient? _client;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private readonly SemaphoreSlim _wake = new(0, 1);
    public bool IsListening { get; private set; }
    public int Port { get; private set; } = 0;
    public event EventHandler<GameEventEnvelope>? EventReceived;
    public event EventHandler<string>? StatusChanged;

    public RelayGameEventSource(string gamePath, string? sharedDiscoveryPath = null)
    {
        _gamePath = gamePath;
        _sharedDiscoveryPath = sharedDiscoveryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SVC-AS", "VividStasisGameInfoRelay", "bridge-relay.json");
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening) return Task.CompletedTask;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsListening = true;
        _loop = RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connected = false;
                foreach (var path in GetDiscoveryPaths())
                {
                    var discovery = await ReadDiscoveryAsync(path, cancellationToken);
                    if (discovery is null) continue;
                    using var client = new TcpClient();
                    _client = client;
                    try
                    {
                        await client.ConnectAsync(discovery.Host, discovery.SubscriberPort, cancellationToken);
                        Port = discovery.SubscriberPort;
                        connected = true;
                        StatusChanged?.Invoke(this, $"Relay subscriber connected on {discovery.Host}:{discovery.SubscriberPort}.");
                        await ReadFramesAsync(client, cancellationToken);
                        break;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke(this, $"Relay at {discovery.Host}:{discovery.SubscriberPort} unavailable: {ex.Message}");
                    }
                    finally { _client = null; }
                }
                if (!connected)
                    StatusChanged?.Invoke(this, "API relay not found; waiting for a running VividStasisGameInfoRelay.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Relay disconnected: {ex.Message}");
            }
            finally { _client = null; }
            if (!cancellationToken.IsCancellationRequested) await DelayOrWakeAsync(cancellationToken);
        }
    }

    private async Task ReadFramesAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var lastSequence = 0L;
        while (!cancellationToken.IsCancellationRequested)
        {
            var header = new byte[4];
            if (!await ReadExactlyAsync(stream, header, cancellationToken)) return;
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is <= 0 or > MaxFrameBytes) throw new InvalidDataException("Invalid relay frame length.");
            var payload = new byte[length];
            if (!await ReadExactlyAsync(stream, payload, cancellationToken)) return;
            while (payload.Length > 0 && payload[^1] == 0) Array.Resize(ref payload, payload.Length - 1);
            var envelope = JsonSerializer.Deserialize<GameEventEnvelope>(payload, JsonOptions);
            if (envelope is null || envelope.ProtocolVersion != 1 || envelope.Sequence <= lastSequence) continue;
            lastSequence = envelope.Sequence;
            EventReceived?.Invoke(this, envelope);
        }
    }

    private static async Task<RelayDiscovery?> ReadDiscoveryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var discovery = await JsonSerializer.DeserializeAsync<RelayDiscovery>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            if (discovery is not null &&
                discovery.ProtocolVersion == 1 &&
                discovery.GamePort == 28745 &&
                discovery.SubscriberPort is >= 1 and <= 65535 &&
                !string.IsNullOrWhiteSpace(discovery.Host))
                return discovery;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    private IEnumerable<string> GetDiscoveryPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_gamePath))
        {
            var path = Path.GetFullPath(Path.Combine(_gamePath, "AutoChartSwitchV2", "bridge-relay.json"));
            if (paths.Add(path)) yield return path;
        }

        if (paths.Add(_sharedDiscoveryPath)) yield return _sharedDiscoveryPath;

        foreach (var process in Process.GetProcessesByName("VividStasisGameInfoRelay"))
        {
            string? path = null;
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executable))
                        path = Path.Combine(Path.GetDirectoryName(executable)!, "AutoChartSwitchV2", "bridge-relay.json");
                }
                catch { }
            }
            if (path is not null && paths.Add(path)) yield return path;
        }
    }

    public Task FindRelayAsync()
    {
        StatusChanged?.Invoke(this, "Searching for a running API relay...");
        try { _client?.Close(); } catch { }
        if (_wake.CurrentCount == 0) _wake.Release();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsListening) return;
        IsListening = false;
        _stop?.Cancel();
        try { _client?.Close(); } catch { }
        if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { }
        _stop?.Dispose();
        _stop = null;
    }

    private async Task DelayOrWakeAsync(CancellationToken cancellationToken)
    {
        await _wake.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (count == 0) return false;
            offset += count;
        }
        return true;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleDecimalConverter());
        options.Converters.Add(new FlexibleInt32Converter());
        options.Converters.Add(new FlexibleInt64Converter());
        options.Converters.Add(new FlexibleGameEventKindConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class FlexibleDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number)) return number;
            if (reader.TokenType == JsonTokenType.String) return decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) ? text : 0m;
            if (reader.TokenType == JsonTokenType.Null) return 0m;
            if (reader.TokenType == JsonTokenType.Number) { reader.Skip(); return 0m; }
            throw new JsonException("Expected a finite decimal.");
        }
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class FlexibleInt32Converter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number) && number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue) return decimal.ToInt32(number);
            if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) && text == decimal.Truncate(text) && text is >= int.MinValue and <= int.MaxValue) return decimal.ToInt32(text);
            throw new JsonException("Expected an integer.");
        }
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class FlexibleInt64Converter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number) && number == decimal.Truncate(number) && number is >= long.MinValue and <= long.MaxValue) return decimal.ToInt64(number);
            if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) && text == decimal.Truncate(text) && text is >= long.MinValue and <= long.MaxValue) return decimal.ToInt64(text);
            throw new JsonException("Expected an integer.");
        }
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class FlexibleGameEventKindConverter : JsonConverter<GameEventKind>
    {
        public override GameEventKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse<GameEventKind>(reader.GetString(), true, out var named)) return named;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numericValue) && numericValue == decimal.Truncate(numericValue) && numericValue is >= int.MinValue and <= int.MaxValue && Enum.IsDefined(typeof(GameEventKind), decimal.ToInt32(numericValue))) return (GameEventKind)decimal.ToInt32(numericValue);
            throw new JsonException("Unsupported game event kind.");
        }

        public override void Write(Utf8JsonWriter writer, GameEventKind value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed record RelayDiscovery
    {
        public int ProtocolVersion { get; init; }
        public string Host { get; init; } = "127.0.0.1";
        public int GamePort { get; init; }
        public int SubscriberPort { get; init; }
        public int ProcessId { get; init; }
    }
}
