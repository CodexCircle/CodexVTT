using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace CodexVtt.Server;

/// <summary>
/// Pagine collegate al core via WebSocket. Gli eventi del bridge vanno a tutte; ogni connessione ha una
/// coda di invio propria, così una pagina lenta non blocca il core né le altre.
/// </summary>
public sealed partial class ClientHub(ILogger<ClientHub> logger)
{
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();

    public int Count => _clients.Count;

    public ClientConnection Add(WebSocket socket)
    {
        var connection = new ClientConnection(socket);
        _clients[connection.Id] = connection;
        LogConnected(_clients.Count);
        return connection;
    }

    public void Remove(ClientConnection connection)
    {
        _clients.TryRemove(connection.Id, out _);
        LogDisconnected(_clients.Count);
    }

    public void Broadcast(string json)
    {
        foreach (var client in _clients.Values)
            client.Post(json);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pagina collegata ({Count} attive)")]
    private partial void LogConnected(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pagina scollegata ({Count} attive)")]
    private partial void LogDisconnected(int count);
}

public sealed class ClientConnection
{
    private readonly WebSocket _socket;
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public ClientConnection(WebSocket socket)
    {
        _socket = socket;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public void Post(string json) => _outgoing.Writer.TryWrite(json);

    public void Complete() => _outgoing.Writer.TryComplete();

    /// <summary>Unico punto che scrive sul socket: WebSocket non ammette invii concorrenti.</summary>
    public async Task PumpAsync(CancellationToken ct)
    {
        await foreach (var json in _outgoing.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (_socket.State != WebSocketState.Open)
                return;
            await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        }
    }
}
