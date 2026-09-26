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
    private TcpClient? _client;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    public bool IsListening { get; private set; }
    public int Port { get; private set; } = 0;
    public event EventHandler<GameEventEnvelope>? EventReceived;
    public event EventHandler<string>? StatusChanged;

    public RelayGameEventSource(string gamePath) => _gamePath = gamePath;

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
                var discovery = await ReadDiscoveryAsync(cancellationToken);
                if (discovery is null)
                {
                    StatusChanged?.Invoke(this, "API relay not found; waiting for a running VividStasisGameInfoRelay.");
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                Port = discovery.SubscriberPort;
                using var client = new TcpClient();
                _client = client;
                await client.ConnectAsync(discovery.Host, discovery.SubscriberPort, cancellationToken);
                StatusChanged?.Invoke(this, $"Relay subscriber connected on {discovery.Host}:{discovery.SubscriberPort}.");
                await ReadFramesAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Relay disconnected: {ex.Message}");
            }
            finally { _client = null; }
            if (!cancellationToken.IsCancellationRequested) await Task.Delay(1000, cancellationToken);
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

    private async Task<RelayDiscovery?> ReadDiscoveryAsync(CancellationToken cancellationToken)
    {
        foreach (var path in GetDiscoveryPaths())
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
        }
        return null;
    }

    private IEnumerable<string> GetDiscoveryPaths()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_gamePath))
            directories.Add(Path.GetFullPath(Path.Combine(_gamePath, "AutoChartSwitchV2")));

        foreach (var process in Process.GetProcessesByName("VividStasisGameInfoRelay"))
        {
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executable))
                        directories.Add(Path.Combine(Path.GetDirectoryName(executable)!, "AutoChartSwitchV2"));
                }
                catch { }
            }
        }

        foreach (var directory in directories)
            yield return Path.Combine(directory, "bridge-relay.json");
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
