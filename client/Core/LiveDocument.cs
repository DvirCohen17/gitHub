using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ICSharpCode.AvalonEdit.Document;

namespace GitHubDemo.Core;

// Someone else's caret and selection in a file.
public sealed class RemoteCursor
{
    public required UserRef User { get; init; }
    public long SessionId { get; init; }
    public int Anchor { get; set; }
    public int Head { get; set; }
    public Color Color => ThemeManager.UserColor(User.Name);
}

// The client side of one file open for live editing.
//
// Every change in the editor becomes a TextOperation sent with the last server
// revision this client has seen. One edit is in flight at a time; later ones
// wait in _pending. Edits from others are transformed past our unconfirmed ones
// before being applied, the mirror image of what the server does, so every copy
// ends up identical. Replies and events are handled in the order the server
// sent them (see Connection).
//
// Undo/redo only touch this user's own edits: the undo stack is transformed past
// everyone else's edits as they arrive (as in ot.js).
public sealed partial class LiveDocument : ObservableObject
{
    private readonly Connection _conn;
    private int _rev;
    private bool _opening = true;
    private TextOperation? _inFlight;
    private readonly List<TextOperation> _pending = new();
    private bool _applyingRemote;

    private readonly List<TextOperation> _undo = new();
    private readonly List<TextOperation> _redo = new();
    private enum UndoState { Normal, Undoing, Redoing }
    private UndoState _undoState = UndoState.Normal;
    private TextOperation? _undoGroup;   // inverse of what an undo/redo did, collected while it runs
    private DateTime _lastEdit;
    private readonly DispatcherTimer _cursorTimer;
    private (int Anchor, int Head)? _localSelection;

    public LiveDocument(Connection conn, long fileId, string path, bool readOnly)
    {
        _conn = conn;
        FileId = fileId;
        _path = path;
        IsReadOnly = readOnly;
        Document.UndoStack.SizeLimit = 0; // undo is ours (see above)
        Document.Changed += OnDocumentChanged;
        _cursorTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => FlushCursor(), Dispatcher.CurrentDispatcher);
    }

    public long FileId { get; }
    public TextDocument Document { get; } = new();
    public ObservableCollection<RemoteCursor> Cursors { get; } = new();
    public ObservableCollection<UserRef> Viewers { get; } = new();
    public bool IsReadOnly { get; }

    [ObservableProperty] private string _path;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _hasUnsyncedChanges;
    [ObservableProperty] private string _error = "";

    public event Action? CursorsChanged;
    public event Action<string>? Lost;   // the file was deleted / access lost

    public bool CanUndo => _undo.Count > 0 && !IsReadOnly && !_opening;
    public bool CanRedo => _redo.Count > 0 && !IsReadOnly && !_opening;

    // ---- opening ----------------------------------------------------------------

    public void Open()
    {
        _opening = true;
        _conn.Request("file.open", new JsonObject { ["fileId"] = FileId }, reply =>
        {
            if (!reply.Bool("ok"))
            {
                Error = reply.Str("error", "Couldn't open the file.");
                Lost?.Invoke(Error);
                return;
            }
            bool hadUnsynced = _inFlight != null || _pending.Count > 0;
            ApplySnapshot(reply.Int("rev"), reply.Str("text"));
            Viewers.Clear();
            foreach (var u in reply.Objects("users")) Viewers.Add(Users.From(u));
            Cursors.Clear();
            foreach (var c in reply.Objects("cursors"))
            {
                var user = Users.Find(c.Long("userId"));
                if (user != null) Cursors.Add(new RemoteCursor { User = user, Anchor = c.Int("anchor"), Head = c.Int("head") });
            }
            CursorsChanged?.Invoke();
            IsLoaded = true;
            if (hadUnsynced) Services.Notifier.Info($"{System.IO.Path.GetFileName(Path)} was reloaded; your last unsent change may be missing.");
        });
    }

    // Replaces the text with the server's copy, changing only the part that
    // differs so the caret stays where it was.
    private void ApplySnapshot(int rev, string text)
    {
        _rev = rev;
        _inFlight = null;
        _pending.Clear();
        _undo.Clear();
        _redo.Clear();
        HasUnsyncedChanges = false;
        _applyingRemote = true;
        try
        {
            string current = Document.Text;
            int prefix = 0;
            int max = Math.Min(current.Length, text.Length);
            while (prefix < max && current[prefix] == text[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix && current[current.Length - 1 - suffix] == text[text.Length - 1 - suffix]) suffix++;
            Document.Replace(prefix, current.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
        }
        finally
        {
            _applyingRemote = false;
        }
        _opening = false;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Close()
    {
        _cursorTimer.Stop();
        Document.Changed -= OnDocumentChanged;
        if (_conn.IsConnected) _conn.Send("file.close", new JsonObject { ["fileId"] = FileId });
    }

    // ---- local edits ------------------------------------------------------------

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_applyingRemote) return;
        if (_opening || IsReadOnly) return;
        int lengthBefore = Document.TextLength - e.InsertionLength + e.RemovalLength;
        var op = TextOperation.FromChange(lengthBefore, e.Offset, e.RemovalLength, e.InsertedText.Text);
        var inverse = TextOperation.FromChange(Document.TextLength, e.Offset, e.InsertionLength, e.RemovedText.Text);

        // shift other people's cursors past our change
        foreach (var c in Cursors)
        {
            c.Anchor = op.TransformIndex(c.Anchor);
            c.Head = op.TransformIndex(c.Head);
        }
        CursorsChanged?.Invoke();

        switch (_undoState)
        {
            case UndoState.Normal:
                // typing in quick succession undoes as one step
                bool group = _undo.Count > 0 && (DateTime.Now - _lastEdit).TotalMilliseconds < 800 && e.RemovalLength + e.InsertionLength == 1;
                if (group) _undo[^1] = TextOperation.Compose(inverse, _undo[^1]);
                else _undo.Add(inverse);
                if (_undo.Count > 500) _undo.RemoveAt(0);
                _redo.Clear();
                _lastEdit = DateTime.Now;
                break;
            default:
                _undoGroup = _undoGroup == null ? inverse : TextOperation.Compose(inverse, _undoGroup);
                break;
        }
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));

        if (_inFlight == null) SendEdit(op);
        else _pending.Add(op);
    }

    private void SendEdit(TextOperation op)
    {
        _inFlight = op;
        HasUnsyncedChanges = true;
        _conn.Request("file.op", new JsonObject { ["fileId"] = FileId, ["rev"] = _rev, ["op"] = op.ToJson() }, OnEditReply);
    }

    private void OnEditReply(JsonObject reply)
    {
        if (!reply.Bool("ok"))
        {
            Error = reply.Str("error");
            if (reply.Str("code") == "forbidden")
            {
                Lost?.Invoke(Error);
                return;
            }
            Open(); // start over from the server's copy
            return;
        }
        if (reply.Bool("resync"))
        {
            ApplySnapshot(reply.Int("rev"), reply.Str("text"));
            Services.Notifier.Info($"{System.IO.Path.GetFileName(Path)} was out of step and has been reloaded.");
            return;
        }
        _rev = reply.Int("rev");
        _inFlight = null;
        if (_pending.Count > 0)
        {
            // everything typed meanwhile goes as one edit
            var next = _pending[0];
            for (int k = 1; k < _pending.Count; k++) next = TextOperation.Compose(next, _pending[k]);
            _pending.Clear();
            SendEdit(next);
        }
        else
        {
            HasUnsyncedChanges = false;
            if (_localSelection != null) _cursorTimer.Start();
        }
    }

    // ---- other people's edits -----------------------------------------------------

    public void OnRemoteEdit(JsonObject message)
    {
        if (_opening) return; // a snapshot is on its way and already includes it
        _rev = message.Int("rev");
        TextOperation op = TextOperation.FromJson(message["op"]);
        if (_inFlight != null)
        {
            (_inFlight, op) = TextOperation.Transform(_inFlight, op);
            for (int k = 0; k < _pending.Count; k++)
                (_pending[k], op) = TextOperation.Transform(_pending[k], op);
        }
        // our undo history now has to skip over their change
        var transformed = op;
        for (int k = _undo.Count - 1; k >= 0; k--) (_undo[k], transformed) = TextOperation.Transform(_undo[k], transformed);
        _undo.RemoveAll(u => u.IsNoop);
        transformed = op;
        for (int k = _redo.Count - 1; k >= 0; k--) (_redo[k], transformed) = TextOperation.Transform(_redo[k], transformed);
        _redo.RemoveAll(u => u.IsNoop);

        _applyingRemote = true;
        try
        {
            op.ApplyTo(Document);
        }
        catch (InvalidOperationException)
        {
            Open(); // out of step (shouldn't happen): reload
            return;
        }
        finally
        {
            _applyingRemote = false;
        }
        foreach (var c in Cursors)
        {
            c.Anchor = op.TransformIndex(c.Anchor);
            c.Head = op.TransformIndex(c.Head);
        }
        CursorsChanged?.Invoke();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void OnRemoteCursor(JsonObject message)
    {
        if (_opening) return;
        var user = Users.Find(message.Long("userId"));
        if (user == null) return;
        int anchor = message.Int("anchor"), head = message.Int("head");
        // positions are in the server's copy; ours has our unconfirmed edits on top
        foreach (var op in UnconfirmedOps())
        {
            anchor = op.TransformIndex(anchor);
            head = op.TransformIndex(head);
        }
        long session = message.Long("sessionId");
        var cursor = Cursors.FirstOrDefault(c => c.SessionId == session && c.User == user);
        if (cursor == null) Cursors.Add(new RemoteCursor { User = user, SessionId = session, Anchor = anchor, Head = head });
        else
        {
            cursor.Anchor = anchor;
            cursor.Head = head;
        }
        CursorsChanged?.Invoke();
    }

    public void OnUsers(JsonObject message)
    {
        Viewers.Clear();
        foreach (var u in message.Objects("users")) Viewers.Add(Users.From(u));
        foreach (var gone in Cursors.Where(c => !Viewers.Contains(c.User)).ToList()) Cursors.Remove(gone);
        CursorsChanged?.Invoke();
    }

    private IEnumerable<TextOperation> UnconfirmedOps()
    {
        if (_inFlight != null) yield return _inFlight;
        foreach (var p in _pending) yield return p;
    }

    // ---- our caret ----------------------------------------------------------------

    // Called by the editor when the caret or selection moves. Sent (throttled)
    // only while we have no unconfirmed edits, so positions match the server's copy.
    public void SetLocalSelection(int anchor, int head)
    {
        _localSelection = (anchor, head);
        if (!_cursorTimer.IsEnabled) _cursorTimer.Start();
    }

    private void FlushCursor()
    {
        if (_localSelection == null || _opening || !_conn.IsConnected)
        {
            _cursorTimer.Stop();
            return;
        }
        if (_inFlight != null) return; // try again after the ack
        _cursorTimer.Stop();
        var (anchor, head) = _localSelection.Value;
        _localSelection = null;
        _conn.Send("file.cursor", new JsonObject { ["fileId"] = FileId, ["rev"] = _rev, ["anchor"] = anchor, ["head"] = head });
    }

    // ---- undo / redo ------------------------------------------------------------------

    public void Undo() => Replay(_undo, _redo, UndoState.Undoing);
    public void Redo() => Replay(_redo, _undo, UndoState.Redoing);

    private void Replay(List<TextOperation> from, List<TextOperation> to, UndoState state)
    {
        if (from.Count == 0 || IsReadOnly || _opening) return;
        var op = from[^1];
        from.RemoveAt(from.Count - 1);
        _undoState = state;
        _undoGroup = null;
        try
        {
            op.ApplyTo(Document); // goes out to the server through OnDocumentChanged
        }
        catch (InvalidOperationException)
        {
            from.Clear();
        }
        finally
        {
            _undoState = UndoState.Normal;
        }
        if (_undoGroup != null) to.Add(_undoGroup);
        _undoGroup = null;
        _lastEdit = DateTime.MinValue;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
