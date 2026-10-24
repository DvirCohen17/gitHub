using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace GitHubDemo.Core;

// An error the server reported for a request ({"ok": false, "code": ..., "error": ...}).
public sealed class ServerException : Exception
{
    public string Code { get; }
    public int RetryAfter { get; }

    public ServerException(string code, string message, int retryAfter = 0) : base(message)
    {
        Code = code;
        RetryAfter = retryAfter;
    }
}

public sealed class ConnectionLostException : Exception
{
    public ConnectionLostException() : base("The connection to the server was lost.") { }
}

// One TCP connection to the server speaking the v2 protocol (docs/PROTOCOL.md):
// '#' + 8-digit length + a JSON object, in both directions.
//
// Everything the server sends (replies and events) is handed to the UI thread in
// the order it arrived. That ordering matters for live editing: an edit's
// acknowledgement and other people's edits must be processed in server order.
public sealed class Connection
{
    private const int MaxMessageSize = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // needed to write values added from code (numbers in an edit, etc.)
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private sealed class Pending
    {
        public string Type = "";
        public TaskCompletionSource<JsonObject>? Task;
        public Action<JsonObject>? Callback;
    }

    private readonly Dispatcher _dispatcher;
    private readonly object _sendLock = new();
    private readonly Dictionary<long, Pending> _pending = new(); // UI thread only
    private TcpClient? _client;
    private NetworkStream? _stream;
    private long _nextRid;
    private int _generation;

    public Connection(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool IsConnected { get; private set; }
    public string Host { get; private set; } = "";
    public int Port { get; private set; }

    // Server events ({"t": "ev.*"}), on the UI thread.
    public event Action<JsonObject>? EventReceived;

    // The connection dropped (not raised for Close()). On the UI thread.
    public event Action? Lost;

    public async Task ConnectAsync(string host, int port, TimeSpan timeout)
    {
        Close();
        var client = new TcpClient { NoDelay = true };
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new TimeoutException($"{host}:{port} didn't answer.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
        _client = client;
        _stream = client.GetStream();
        Host = host;
        Port = port;
        IsConnected = true;
        int generation = ++_generation;
        var stream = _stream;
        new Thread(() => ReadLoop(stream, generation)) { IsBackground = true, Name = "server reader" }.Start();
    }

    public void Close()
    {
        _generation++;
        IsConnected = false;
        try { _client?.Close(); } catch { }
        _client = null;
        _stream = null;
        FailPending();
    }

    // Sends a request and waits for its reply. Throws ServerException for an error
    // reply, ConnectionLostException if the connection drops first.
    public Task<JsonObject> RequestAsync(string type, JsonObject? fields = null, int timeoutMs = 20000)
    {
        var tcs = new TaskCompletionSource<JsonObject>();
        long rid = Register(new Pending { Type = type, Task = tcs });
        if (!Write(type, fields, rid))
        {
            Complete(rid, null);
        }
        else if (timeoutMs > 0)
        {
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(timeoutMs), DispatcherPriority.Normal, (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                if (_pending.Remove(rid))
                    tcs.TrySetException(new TimeoutException("The server didn't answer in time."));
            }, _dispatcher);
            tcs.Task.ContinueWith(_ => _dispatcher.BeginInvoke(timer.Stop));
        }
        return tcs.Task;
    }

    // Sends a request; the reply (ok or not) is passed to onReply in order with
    // events. Used by live editing, which depends on that order.
    public void Request(string type, JsonObject fields, Action<JsonObject> onReply)
    {
        long rid = Register(new Pending { Type = type, Callback = onReply });
        if (!Write(type, fields, rid)) _pending.Remove(rid);
    }

    // Fire-and-forget (cursor positions, typing indicator).
    public void Send(string type, JsonObject? fields = null) => Write(type, fields, 0);

    private long Register(Pending pending)
    {
        long rid = ++_nextRid;
        _pending[rid] = pending;
        return rid;
    }

    private bool Write(string type, JsonObject? fields, long rid)
    {
        var stream = _stream;
        if (stream == null) return false;
        var message = new JsonObject { ["t"] = type };
        if (rid != 0) message["rid"] = rid;
        if (fields != null)
        {
            foreach (var (key, value) in fields.ToList())
            {
                fields.Remove(key);
                message[key] = value;
            }
        }
        byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString(WireOptions));
        byte[] frame = new byte[9 + payload.Length];
        Encoding.ASCII.GetBytes("#" + payload.Length.ToString("D8"), 0, 9, frame, 0);
        Buffer.BlockCopy(payload, 0, frame, 9, payload.Length);
        try
        {
            lock (_sendLock)
            {
                stream.Write(frame, 0, frame.Length);
            }
            return true;
        }
        catch (Exception) when (!IsConnected || stream != _stream)
        {
            return false;
        }
        catch (Exception)
        {
            OnLost(_generation);
            return false;
        }
    }

    private void ReadLoop(NetworkStream stream, int generation)
    {
        try
        {
            byte[] header = new byte[9];
            while (true)
            {
                ReadExactly(stream, header, 9);
                if (header[0] != (byte)'#') throw new IOException("Unexpected data from the server.");
                int length = int.Parse(Encoding.ASCII.GetString(header, 1, 8));
                if (length <= 0 || length > MaxMessageSize) throw new IOException("Bad message length from the server.");
                byte[] payload = new byte[length];
                ReadExactly(stream, payload, length);
                JsonObject? message = null;
                try
                {
                    message = JsonNode.Parse(payload) as JsonObject;
                }
                catch (JsonException)
                {
                    // not JSON (a reply meant for installers): ignore
                }
                if (message != null)
                    _dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Dispatch(message, generation));
            }
        }
        catch (Exception)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, () => OnLost(generation));
        }
    }

    private static void ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int got = stream.Read(buffer, read, count - read);
            if (got == 0) throw new IOException("The server closed the connection.");
            read += got;
        }
    }

    private void Dispatch(JsonObject message, int generation)
    {
        if (generation != _generation) return; // from a connection we already dropped
        if (message.TryGetPropertyValue("rid", out var ridNode) && ridNode is JsonValue rv && rv.TryGetValue(out long rid))
        {
            Complete(rid, message);
            return;
        }
        EventReceived?.Invoke(message);
    }

    private void Complete(long rid, JsonObject? reply)
    {
        if (!_pending.Remove(rid, out var pending)) return;
        if (reply == null)
        {
            pending.Task?.TrySetException(new ConnectionLostException());
            return;
        }
        if (pending.Callback != null)
        {
            pending.Callback(reply);
            return;
        }
        if (reply.Bool("ok"))
            pending.Task!.TrySetResult(reply);
        else
            pending.Task!.TrySetException(new ServerException(reply.Str("code", "error"), reply.Str("error", "The request failed."), reply.Int("retryAfter")));
    }

    private void OnLost(int generation)
    {
        if (generation != _generation || !IsConnected) return;
        IsConnected = false;
        try { _client?.Close(); } catch { }
        _client = null;
        _stream = null;
        _generation++;
        FailPending();
        Lost?.Invoke();
    }

    private void FailPending()
    {
        var pending = _pending.Values.ToList();
        _pending.Clear();
        foreach (var p in pending)
        {
            p.Task?.TrySetException(new ConnectionLostException());
        }
    }
}
