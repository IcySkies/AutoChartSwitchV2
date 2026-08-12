using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoChartSwitch.Core;

namespace AutoChartSwitch.GameBridge;

public sealed class TcpGameEventSource : IGameEventSource
{
    public const int SupportedProtocolVersion = 1;
    private const int MaxFrameBytes = 1024 * 1024;
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    public bool IsListening { get; private set; }
    public int Port { get; }
    public event EventHandler<GameEventEnvelope>? EventReceived;
    public event EventHandler<string>? StatusChanged;

    public TcpGameEventSource(int port = 28745)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new GameMakerInt32JsonConverter());
        options.Converters.Add(new GameMakerInt64JsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening) return Task.CompletedTask;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        IsListening = true;
        RaiseStatus($"Game bridge listening on 127.0.0.1:{Port}.");
        _ = AcceptLoopAsync(_stop.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                RaiseStatus($"Game transport connected from {client.Client.RemoteEndPoint}; waiting for the first event.");
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { RaiseStatus($"Game bridge listener failed: {ex.Message}"); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "game";
        long lastSequence = 0;
        var receivedFirstEvent = false;
        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var header = new byte[sizeof(int)];
                    if (!await ReadExactlyAsync(stream, header, cancellationToken)) break;
                    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (length is <= 0 or > MaxFrameBytes)
                    {
                        RaiseStatus($"Rejected invalid game bridge frame length {length} from {endpoint}.");
                        break;
                    }

                    var payload = new byte[length];
                    if (!await ReadExactlyAsync(stream, payload, cancellationToken)) break;
                    while (payload.Length > 0 && payload[^1] == 0)
                        Array.Resize(ref payload, payload.Length - 1);
                    GameEventEnvelope? envelope;
                    try { envelope = JsonSerializer.Deserialize<GameEventEnvelope>(payload, _jsonOptions); }
                    catch (JsonException ex)
                    {
                        RaiseStatus($"Rejected malformed game event: {ex.Message}");
                        continue;
                    }

                    if (envelope is null || envelope.ProtocolVersion != SupportedProtocolVersion)
                    {
                        RaiseStatus("Rejected game event with an unsupported protocol version.");
                        continue;
                    }

                    // GameMaker restarts its sequence at one on every TCP reconnect.
                    // De-duplication therefore belongs to this connection, not the listener lifetime.
                    if (envelope.Sequence <= lastSequence) continue;
                    lastSequence = envelope.Sequence;
                    if (!receivedFirstEvent)
                    {
                        receivedFirstEvent = true;
                        RaiseStatus($"Game bridge active from {endpoint}.");
                    }
                    try { EventReceived?.Invoke(this, envelope); }
                    catch (Exception ex) { RaiseStatus($"Game event handler failed: {ex.Message}"); }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { RaiseStatus("Game bridge disconnected; waiting for the game to reconnect."); }
        catch (SocketException) { RaiseStatus("Game bridge disconnected; waiting for the game to reconnect."); }
    }

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

    public Task StopAsync()
    {
        if (!IsListening) return Task.CompletedTask;
        IsListening = false;
        _stop?.Cancel();
        _listener?.Stop();
        _stop?.Dispose();
        _stop = null;
        _listener = null;
        RaiseStatus("Game bridge stopped.");
        return Task.CompletedTask;
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);

    public ValueTask DisposeAsync()
    {
        StopAsync();
        return ValueTask.CompletedTask;
    }
}
