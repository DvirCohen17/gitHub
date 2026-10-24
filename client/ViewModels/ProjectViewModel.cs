using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;
using Microsoft.Win32;

namespace GitHubDemo.ViewModels;

// A project: file tree, live editor (tabs, split view), chat, members,
// version history and running code.
public sealed partial class ProjectViewModel : PageViewModel
{
    private readonly Dictionary<long, LiveDocument> _documents = new();
    private readonly HashSet<string> _expanded = new();
    private readonly DispatcherTimer _typingTimer;
    private readonly Dictionary<long, DateTime> _typing = new();
    private List<(long Id, string Path)> _files = new();
    private DateTime _lastTypingSent;
    private bool _opened;

    public ProjectViewModel(long projectId)
    {
        ProjectId = projectId;
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _typingTimer.Tick += (_, _) => UpdateTypingText();
    }

    public long ProjectId { get; }
    public override string Title => Name;
    public override string NavKey => "projects";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private string _name = "";
    [ObservableProperty] private string _language = "";
    [ObservableProperty] private string _languageKey = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPublic))] private string _visibility = "private";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(IsAdmin), nameof(IsOwner), nameof(IsViewer), nameof(RoleLabel))]
    private string _role = "viewer";
    [ObservableProperty] private bool _loaded;

    public bool IsPublic => Visibility == "public";
    public bool CanEdit => Role is "owner" or "admin" or "member";
    public bool IsAdmin => Role is "owner" or "admin";
    public bool IsOwner => Role == "owner";
    public bool IsViewer => Role == "viewer";
    public string RoleLabel => Role switch { "owner" => "Owner", "admin" => "Admin", "member" => "Member", _ => "Watching" };

    public ObservableCollection<TreeNode> Tree { get; } = new();
    public ObservableCollection<MemberItem> Members { get; } = new();
    public ObservableCollection<EditorTab> Tabs { get; } = new();
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<VersionItem> Versions { get; } = new();

    // ---- right panel: chat / people / history ----
    [ObservableProperty] private string _sidePanel = "chat";

    // ---- editor panes ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTab), nameof(ActiveDocument))]
    private EditorTab? _activeTab;

    [ObservableProperty] private EditorTab? _splitTab;
    [ObservableProperty] private bool _isSplit;

    public bool HasActiveTab => ActiveTab != null;
    public LiveDocument? ActiveDocument => ActiveTab?.Document;
    public bool HasFiles => _files.Count > 0;

    partial void OnActiveTabChanged(EditorTab? oldValue, EditorTab? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null)
        {
            newValue.IsActive = true;
            SelectNode(newValue.FileId);
            if (Services.Connection.IsConnected)
                Services.Connection.Send("presence.set", new JsonObject { ["projectId"] = ProjectId, ["fileId"] = newValue.FileId });
        }
        if (SidePanel == "history") _ = LoadVersionsAsync();
        RunCodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSidePanelChanged(string value)
    {
        if (value == "history") _ = LoadVersionsAsync();
    }

    // ---- loading ------------------------------------------------------------------

    protected override void Subscribe()
    {
        On("ev.tree", e => { if (e.Long("projectId") == ProjectId) ApplyTree(e); });
        On("ev.project", e => { if (e.Obj("project") is { } p && p.Long("id") == ProjectId) ApplyProject(p); });
        On("ev.project.deleted", e => { if (e.Long("id") == ProjectId) Kicked(e.Str("reason", "The project was deleted.")); });
        On("ev.project.removed", e => { if (e.Long("id") == ProjectId) Kicked(e.Str("reason", "You were removed from the project.")); });
        On("ev.file.op", e => DocumentFor(e)?.OnRemoteEdit(e));
        On("ev.file.cursor", e => DocumentFor(e)?.OnRemoteCursor(e));
        On("ev.file.users", e => DocumentFor(e)?.OnUsers(e));
        On("ev.file.deleted", e => FileDeleted(e.Long("fileId")));
        On("ev.chat", e => { if (e.Long("projectId") == ProjectId && e.Obj("message") is { } m) ChatEvent(m); });
        On("ev.chat.typing", e =>
        {
            if (e.Long("projectId") != ProjectId || e.Obj("user") is not { } u) return;
            _typing[Users.From(u).Id] = DateTime.Now;
            UpdateTypingText();
            _typingTimer.Start();
        });
        On("ev.presence", _ => UpdateEditorsInTree());
        On("ev.run.output", e => { if (e.Str("runId") == RunId) AppendOutput(e.Str("stream"), e.Str("text")); });
        On("ev.run.exit", e => { if (e.Str("runId") == RunId) RunFinished(e); });
    }

    private LiveDocument? DocumentFor(JsonObject e) => _documents.GetValueOrDefault(e.Long("fileId"));

    public override async Task LoadAsync()
    {
        bool ok = await RunAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("project.open", new JsonObject { ["id"] = ProjectId });
            _opened = true;
            ApplyProject(reply.Obj("project")!);
            Role = reply.Str("role");
            ApplyTree(reply);
            await LoadChatAsync();
        });
        if (!ok)
        {
            Services.Notifier.Error(Error);
            Services.Main.Navigate(new HomeViewModel());
            return;
        }
        // reopen files that were open (after a reconnect)
        foreach (var doc in _documents.Values) doc.Open();
        if (Tabs.Count == 0)
        {
            // open the first file so the editor isn't empty
            var first = _files.OrderBy(f => f.Path.Contains('/') ? 1 : 0).ThenBy(f => f.Path).FirstOrDefault();
            if (first.Id > 0) OpenFile(first.Id);
        }
        Loaded = true;
    }

    public override void OnLeave()
    {
        foreach (var doc in _documents.Values) doc.Close();
        _documents.Clear();
        _typingTimer.Stop();
        if (IsRunning && RunId != null) Services.Connection.Send("run.stop", new JsonObject { ["runId"] = RunId });
        if (_opened && Services.Connection.IsConnected) Services.Connection.Send("project.close", new JsonObject { ["id"] = ProjectId });
        base.OnLeave();
    }

    private void ApplyProject(JsonObject p)
    {
        Name = p.Str("name");
        LanguageKey = p.Str("language");
        Language = CodeStyles.ProjectLanguageLabel(LanguageKey);
        Description = p.Str("description");
        Visibility = p.Str("visibility");
        if (p.Str("myRole") is { Length: > 0 } role) Role = role;
        var members = p.Objects("members").Select(m => new MemberItem { User = Users.From(m.Obj("user")), Role = m.Str("role") }).ToList();
        Members.Clear();
        foreach (var m in members.OrderBy(m => m.User.IsOnline ? 0 : 1).ThenBy(m => m.Role == "owner" ? 0 : m.Role == "admin" ? 1 : 2).ThenBy(m => m.User.Name))
            Members.Add(m);
        OnPropertyChanged(nameof(OnlineCount));
        UpdateEditorsInTree();
    }

    public int OnlineCount => Members.Count(m => m.User.IsOnline);

    private void Kicked(string reason)
    {
        _opened = false;
        Services.Main.Navigate(new HomeViewModel());
        _ = Services.Dialogs.AlertAsync(Name, reason);
    }

    // ---- file tree ----------------------------------------------------------------

    private void ApplyTree(JsonObject e)
    {
        foreach (var node in Tree.SelectMany(n => n.Descendants().Prepend(n)))
        {
            if (node.IsFolder && node.IsExpanded) _expanded.Add(node.Path);
            else if (node.IsFolder) _expanded.Remove(node.Path);
        }
        _files = e.Objects("files").Select(f => (f.Long("id"), f.Str("path"))).ToList();
        var roots = TreeNode.Build(_files, e.Strings("folders"), _expanded);
        Tree.Clear();
        foreach (var r in roots) Tree.Add(r);
        // renamed files: update open tabs
        foreach (var (id, path) in _files)
            if (_documents.TryGetValue(id, out var doc) && doc.Path != path) doc.Path = path;
        if (ActiveTab != null) SelectNode(ActiveTab.FileId);
        UpdateEditorsInTree();
        OnPropertyChanged(nameof(HasFiles));
    }

    private IEnumerable<TreeNode> AllNodes => Tree.SelectMany(n => n.Descendants().Prepend(n));

    private void SelectNode(long fileId)
    {
        foreach (var node in AllNodes) node.IsSelected = !node.IsFolder && node.FileId == fileId;
    }

    // "dana is editing" markers next to files.
    private void UpdateEditorsInTree()
    {
        var byFile = Members.Where(m => !m.User.IsMe && m.User.IsOnline && m.User.ProjectId == ProjectId && m.User.File.Length > 0)
            .GroupBy(m => m.User.File).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(m => m.User.Name)));
        foreach (var node in AllNodes.Where(n => !n.IsFolder))
            node.Editors = byFile.GetValueOrDefault(node.Path, "");
        OnPropertyChanged(nameof(OnlineCount));
    }

    [RelayCommand]
    private void OpenNode(TreeNode? node)
    {
        if (node == null) return;
        if (node.IsFolder) node.IsExpanded = !node.IsExpanded;
        else OpenFile(node.FileId);
    }

    public void OpenFile(long fileId, bool inSplit = false)
    {
        var existing = Tabs.FirstOrDefault(t => t.FileId == fileId);
        if (existing == null)
        {
            string path = _files.FirstOrDefault(f => f.Id == fileId).Path ?? "";
            var doc = new LiveDocument(Services.Connection, fileId, path, !CanEdit);
            doc.Lost += reason =>
            {
                Services.Notifier.Error(reason);
                CloseTabFor(fileId);
            };
            _documents[fileId] = doc;
            existing = new EditorTab(doc);
            Tabs.Add(existing);
            doc.Open();
        }
        if (inSplit)
        {
            SplitTab = existing;
            IsSplit = true;
        }
        else
        {
            ActiveTab = existing;
        }
    }

    [RelayCommand] private void OpenInSplit(TreeNode? node) { if (node is { IsFolder: false }) OpenFile(node.FileId, inSplit: true); }

    [RelayCommand]
    private void CloseTab(EditorTab? tab)
    {
        if (tab == null) return;
        CloseTabFor(tab.FileId);
    }

    private void CloseTabFor(long fileId)
    {
        var tab = Tabs.FirstOrDefault(t => t.FileId == fileId);
        if (tab == null) return;
        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (_documents.Remove(fileId, out var doc)) doc.Close();
        if (SplitTab == tab)
        {
            SplitTab = null;
            IsSplit = false;
        }
        if (ActiveTab == tab) ActiveTab = Tabs.Count > 0 ? Tabs[Math.Min(index, Tabs.Count - 1)] : null;
    }

    [RelayCommand] private void ActivateTab(EditorTab? tab) { if (tab != null) ActiveTab = tab; }

    [RelayCommand]
    private void ToggleSplit()
    {
        if (IsSplit)
        {
            IsSplit = false;
            SplitTab = null;
            return;
        }
        if (Tabs.Count == 0) return;
        SplitTab = Tabs.FirstOrDefault(t => t != ActiveTab) ?? ActiveTab;
        IsSplit = true;
    }

    [RelayCommand] private void ShowInSplit(EditorTab? tab) { if (tab != null) { SplitTab = tab; IsSplit = true; } }

    private void FileDeleted(long fileId)
    {
        if (Tabs.Any(t => t.FileId == fileId))
        {
            Services.Notifier.Info($"{Path.GetFileName(_documents.GetValueOrDefault(fileId)?.Path ?? "A file")} was deleted.");
            CloseTabFor(fileId);
        }
    }

    // ---- creating, renaming, deleting ------------------------------------------------

    private string FolderOf(TreeNode? node) => node == null ? "" : node.IsFolder ? node.Path : (node.Path.Contains('/') ? node.Path[..node.Path.LastIndexOf('/')] : "");

    private static string? CheckName(string name)
    {
        if (name.Length == 0) return "Enter a name.";
        if (name.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0) return "Names can't contain : * ? \" < > |";
        if (name.Split('/', '\\').Any(s => s.Trim() is "." or "..")) return "Names can't be '.' or '..'.";
        return null;
    }

    [RelayCommand]
    private async Task NewFileAsync(TreeNode? at)
    {
        string folder = FolderOf(at);
        string suggestion = "untitled" + CodeStyles.DefaultExtension(LanguageKey);
        string? name = await Services.Dialogs.PromptAsync("New file", folder.Length > 0 ? $"File name (in {folder}/)" : "File name",
            suggestion, "Create", CheckName, "Use / to put it in a folder, e.g. src/main.cpp.");
        if (name == null) return;
        if (!Path.HasExtension(name)) name += CodeStyles.DefaultExtension(LanguageKey);
        string path = folder.Length > 0 ? folder + "/" + name : name;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("file.create", new JsonObject { ["projectId"] = ProjectId, ["path"] = path });
            var file = reply.Obj("file")!;
            _files.Add((file.Long("id"), file.Str("path")));
            ExpandTo(file.Str("path"));
            OpenFile(file.Long("id"));
        });
    }

    [RelayCommand]
    private async Task NewFolderAsync(TreeNode? at)
    {
        string folder = FolderOf(at);
        string? name = await Services.Dialogs.PromptAsync("New folder", folder.Length > 0 ? $"Folder name (in {folder}/)" : "Folder name", "", "Create", CheckName);
        if (name == null) return;
        string path = folder.Length > 0 ? folder + "/" + name : name;
        await TryAsync(async () =>
        {
            await Services.Connection.RequestAsync("folder.create", new JsonObject { ["projectId"] = ProjectId, ["path"] = path });
            ExpandTo(path + "/x");
        });
    }

    private void ExpandTo(string path)
    {
        int slash = path.IndexOf('/');
        while (slash > 0)
        {
            _expanded.Add(path[..slash]);
            slash = path.IndexOf('/', slash + 1);
        }
        foreach (var node in AllNodes.Where(n => n.IsFolder && _expanded.Contains(n.Path))) node.IsExpanded = true;
    }

    [RelayCommand]
    private async Task RenameAsync(TreeNode? node)
    {
        if (node == null) return;
        string? path = await Services.Dialogs.PromptAsync(node.IsFolder ? "Rename folder" : "Rename file", "New name or path", node.Path,
            "Rename", CheckName, "Change the folder part to move it, e.g. src/" + node.Name + ".");
        if (path == null || path == node.Path) return;
        await TryAsync(() => node.IsFolder
            ? Services.Connection.RequestAsync("folder.rename", new JsonObject { ["projectId"] = ProjectId, ["path"] = node.Path, ["newPath"] = path })
            : Services.Connection.RequestAsync("file.rename", new JsonObject { ["fileId"] = node.FileId, ["path"] = path }));
    }

    [RelayCommand]
    private async Task DeleteAsync(TreeNode? node)
    {
        if (node == null) return;
        string message = node.IsFolder
            ? $"Delete the folder {node.Path} and the {node.Descendants().Count(d => !d.IsFolder)} file(s) in it? Everyone in the project loses them."
            : $"Delete {node.Path}? Everyone in the project loses it, including its history.";
        if (!await Services.Dialogs.ConfirmAsync(node.IsFolder ? "Delete folder" : "Delete file", message, "Delete", danger: true)) return;
        await TryAsync(() => node.IsFolder
            ? Services.Connection.RequestAsync("folder.delete", new JsonObject { ["projectId"] = ProjectId, ["path"] = node.Path })
            : Services.Connection.RequestAsync("file.delete", new JsonObject { ["fileId"] = node.FileId }));
    }

    // ---- import / export ----------------------------------------------------------------

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { FileName = MakeSafeFileName(Name) + ".zip", Filter = "Zip archive|*.zip" };
        if (dialog.ShowDialog() != true) return;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("project.export", new JsonObject { ["id"] = ProjectId }, 60000);
            await using var stream = File.Create(dialog.FileName);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (string folder in reply.Strings("folders")) zip.CreateEntry(folder + "/");
            foreach (var f in reply.Objects("files"))
            {
                var entry = zip.CreateEntry(f.Str("path"), CompressionLevel.Optimal);
                await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                await writer.WriteAsync(f.Str("text"));
            }
            Services.Notifier.Success($"Exported {reply.Objects("files").Count()} file(s) to {Path.GetFileName(dialog.FileName)}.");
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Zip archive|*.zip", Title = "Import files from a zip" };
        if (dialog.ShowDialog() != true) return;
        await TryAsync(async () =>
        {
            var files = new JsonArray();
            var folders = new JsonArray();
            int skippedBinary = 0;
            using (var zip = ZipFile.OpenRead(dialog.FileName))
            {
                // A zip of one folder ("project/...") imports that folder's contents.
                string prefix = CommonRoot(zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList());
                foreach (var entry in zip.Entries)
                {
                    string path = entry.FullName.Replace('\\', '/');
                    if (prefix.Length > 0) path = path.Substring(prefix.Length);
                    if (path.Length == 0 || path.StartsWith("__MACOSX") || path.Split('/').Any(s => s.StartsWith(".git"))) continue;
                    if (path.EndsWith('/'))
                    {
                        folders.Add(path.TrimEnd('/'));
                        continue;
                    }
                    if (entry.Length > 2 * 1024 * 1024) { skippedBinary++; continue; }
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string text = await reader.ReadToEndAsync();
                    if (text.Contains('\0')) { skippedBinary++; continue; } // binary file
                    files.Add(new JsonObject { ["path"] = path, ["text"] = text });
                }
            }
            if (files.Count == 0) throw new InvalidOperationException("The zip has no text files to import.");
            var reply = await Services.Connection.RequestAsync("project.import",
                new JsonObject { ["id"] = ProjectId, ["files"] = files, ["folders"] = folders }, 120000);
            string skipped = reply.Long("skipped") > 0 ? $", {reply.Long("skipped")} already existed" : "";
            string binary = skippedBinary > 0 ? $", {skippedBinary} binary or very large file(s) left out" : "";
            Services.Notifier.Success($"Imported {reply.Long("created")} file(s){skipped}{binary}.");
        });
    }

    private static string CommonRoot(List<string> paths)
    {
        if (paths.Count == 0) return "";
        int slash = paths[0].IndexOf('/');
        if (slash <= 0) return "";
        string root = paths[0][..(slash + 1)];
        return paths.All(p => p.StartsWith(root)) ? root : "";
    }

    private static string MakeSafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ---- project settings, sharing, leaving ---------------------------------------------

    [RelayCommand] private void Back() => Services.Main.Navigate(new HomeViewModel());
    [RelayCommand] private void OpenTasks() => Services.Main.OpenTasks(this);

    [RelayCommand]
    private async Task EditProjectAsync()
    {
        await Views.ProjectDialog.EditAsync(ProjectId, readOnly: !IsAdmin);
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (IsPublic)
        {
            string link = $"githubdemo://project/{ProjectId}";
            Clipboard.SetText(link);
            Services.Notifier.Success("Link copied. Anyone with gitHub Demo can watch this public project.");
        }
        if (!IsAdmin) return;
        await Views.ProjectDialog.EditAsync(ProjectId, readOnly: false, startOnSharing: true);
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        string extra = IsOwner && Members.Count > 1 ? " Ownership goes to another admin or member." : IsOwner ? " You're the only member, so the project will be deleted." : "";
        if (!await Services.Dialogs.ConfirmAsync("Leave project", $"Leave {Name}?{extra}", "Leave", danger: true)) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("project.leave", new JsonObject { ["id"] = ProjectId })))
        {
            _opened = false;
            Services.Main.Navigate(new HomeViewModel());
        }
    }

    [RelayCommand]
    private async Task DeleteProjectAsync()
    {
        string? typed = await Services.Dialogs.PromptAsync("Delete project",
            "Type the project's name to confirm", "", "Delete forever",
            text => text == Name ? null : "That isn't the project's name.",
            $"This deletes {Name} for everyone: files, history, chat and tasks. It can't be undone.");
        if (typed == null) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("project.delete", new JsonObject { ["id"] = ProjectId })))
        {
            _opened = false;
            Services.Notifier.Success($"{Name} was deleted.");
            Services.Main.Navigate(new HomeViewModel());
        }
    }

    [RelayCommand] private void OpenMember(MemberItem? member) { if (member != null) Services.Main.OpenProfile(member.User.Name); }

    // ---- chat --------------------------------------------------------------------------

    [ObservableProperty] private string _chatInput = "";
    [ObservableProperty] private string _typingText = "";
    [ObservableProperty] private bool _hasOlderMessages;
    [ObservableProperty] private int _unreadChat;
    public ObservableCollection<UserRef> MentionSuggestions { get; } = new();
    [ObservableProperty] private bool _showMentions;

    public event Action? ChatScrollToEnd;

    private async Task LoadChatAsync()
    {
        var reply = await Services.Connection.RequestAsync("chat.history", new JsonObject { ["projectId"] = ProjectId, ["limit"] = 80 });
        Messages.Clear();
        foreach (var m in reply.Objects("messages")) Messages.Add(ChatMessageItem.From(m));
        HasOlderMessages = reply.Bool("more");
        RegroupMessages();
        ChatScrollToEnd?.Invoke();
    }

    [RelayCommand]
    private async Task LoadOlderAsync()
    {
        if (Messages.Count == 0) return;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("chat.history",
                new JsonObject { ["projectId"] = ProjectId, ["limit"] = 80, ["before"] = Messages[0].Id });
            var older = reply.Objects("messages").Select(ChatMessageItem.From).ToList();
            for (int k = older.Count - 1; k >= 0; k--) Messages.Insert(0, older[k]);
            HasOlderMessages = reply.Bool("more");
            RegroupMessages();
        });
    }

    // Consecutive messages from one person within 5 minutes share a header.
    private void RegroupMessages()
    {
        for (int k = 0; k < Messages.Count; k++)
        {
            var m = Messages[k];
            var prev = k > 0 ? Messages[k - 1] : null;
            m.ShowHeader = prev == null || prev.User != m.User || (m.Time - prev.Time).TotalMinutes > 5;
        }
    }

    private void ChatEvent(JsonObject message)
    {
        long id = message.Long("id");
        var existing = Messages.FirstOrDefault(m => m.Id == id);
        if (existing != null)
        {
            existing.Update(message);
            return;
        }
        var item = ChatMessageItem.From(message);
        Messages.Add(item);
        RegroupMessages();
        _typing.Remove(item.User.Id);
        UpdateTypingText();
        if (SidePanel != "chat") UnreadChat++;
        ChatScrollToEnd?.Invoke();
        Services.Notifier.ChatMessage(Name, ProjectId, item.User, item.Text);
    }

    partial void OnChatInputChanged(string value)
    {
        // typing indicator, at most every 2 s
        if (value.Length > 0 && (DateTime.Now - _lastTypingSent).TotalSeconds > 2 && Services.Connection.IsConnected && CanEdit)
        {
            _lastTypingSent = DateTime.Now;
            Services.Connection.Send("chat.typing", new JsonObject { ["projectId"] = ProjectId });
        }
        // @mention suggestions for the word being typed
        var match = Regex.Match(value, @"(?:^|\s)@([A-Za-z0-9._-]*)$");
        MentionSuggestions.Clear();
        if (match.Success)
        {
            string prefix = match.Groups[1].Value;
            foreach (var m in Members.Where(m => !m.User.IsMe && m.User.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(6))
                MentionSuggestions.Add(m.User);
        }
        ShowMentions = MentionSuggestions.Count > 0;
    }

    [RelayCommand]
    private void InsertMention(UserRef? user)
    {
        if (user == null) return;
        ChatInput = Regex.Replace(ChatInput, @"@([A-Za-z0-9._-]*)$", "@" + user.Name + " ");
        ShowMentions = false;
    }

    partial void OnSidePanelChanging(string value)
    {
        if (value == "chat") UnreadChat = 0;
    }

    [RelayCommand]
    private async Task SendChatAsync()
    {
        string text = ChatInput.Trim();
        if (text.Length == 0) return;
        ChatInput = "";
        ShowMentions = false;
        bool ok = await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("chat.send", new JsonObject { ["projectId"] = ProjectId, ["text"] = text });
            if (reply.Obj("message") is { } m && Messages.All(x => x.Id != m.Long("id")))
            {
                Messages.Add(ChatMessageItem.From(m));
                RegroupMessages();
                ChatScrollToEnd?.Invoke();
            }
        });
        if (!ok) ChatInput = text; // keep what they typed
    }

    [RelayCommand]
    private void EditMessage(ChatMessageItem? message)
    {
        if (message == null || !message.IsMine) return;
        message.EditDraft = message.Text;
        message.IsEditing = true;
    }

    [RelayCommand] private void CancelEdit(ChatMessageItem? message) { if (message != null) message.IsEditing = false; }

    [RelayCommand]
    private async Task SaveEditAsync(ChatMessageItem? message)
    {
        if (message == null) return;
        string text = message.EditDraft.Trim();
        if (text.Length == 0 || text == message.Text)
        {
            message.IsEditing = false;
            return;
        }
        if (await TryAsync(async () =>
            {
                var reply = await Services.Connection.RequestAsync("chat.edit", new JsonObject { ["id"] = message.Id, ["text"] = text });
                if (reply.Obj("message") is { } m) message.Update(m);
            }))
        {
            message.IsEditing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessageItem? message)
    {
        if (message == null) return;
        if (!await Services.Dialogs.ConfirmAsync("Delete message", "Delete this message for everyone?", "Delete", danger: true)) return;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("chat.delete", new JsonObject { ["id"] = message.Id });
            if (reply.Obj("message") is { } m) message.Update(m);
        });
    }

    [RelayCommand] private void CopyMessage(ChatMessageItem? message) { if (message != null) Clipboard.SetText(message.Text); }

    private void UpdateTypingText()
    {
        foreach (var stale in _typing.Where(t => (DateTime.Now - t.Value).TotalSeconds > 4).Select(t => t.Key).ToList()) _typing.Remove(stale);
        var names = _typing.Keys.Select(id => Users.Find(id)?.Name).Where(n => n != null).ToList();
        TypingText = names.Count switch
        {
            0 => "",
            1 => $"{names[0]} is typing...",
            2 => $"{names[0]} and {names[1]} are typing...",
            _ => "Several people are typing...",
        };
        if (names.Count == 0) _typingTimer.Stop();
    }

    // ---- version history ------------------------------------------------------------------

    [ObservableProperty] private VersionItem? _selectedVersion;
    [ObservableProperty] private string _versionPreview = "";

    private async Task LoadVersionsAsync()
    {
        Versions.Clear();
        VersionPreview = "";
        SelectedVersion = null;
        if (ActiveTab == null) return;
        long fileId = ActiveTab.FileId;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("file.versions", new JsonObject { ["fileId"] = fileId });
            if (ActiveTab?.FileId != fileId) return;
            foreach (var v in reply.Objects("versions"))
            {
                Versions.Add(new VersionItem
                {
                    Id = v.Long("id"), Label = v.Str("label"), Time = v.Time("createdAt"),
                    User = v.Obj("user") is { } u ? Users.From(u) : null, Size = v.Long("size"),
                });
            }
        });
    }

    [RelayCommand] private Task RefreshVersions() => LoadVersionsAsync();

    partial void OnSelectedVersionChanged(VersionItem? value)
    {
        VersionPreview = "";
        if (value == null) return;
        _ = TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("file.version", new JsonObject { ["versionId"] = value.Id });
            if (SelectedVersion == value) VersionPreview = reply.Str("text");
        });
    }

    [RelayCommand]
    private async Task SaveCheckpointAsync()
    {
        if (ActiveTab == null) return;
        string? label = await Services.Dialogs.PromptAsync("Save version", "Name this version", "", "Save",
            text => text.Length is 0 or > 60 ? "1-60 characters." : null, "Named versions are kept forever; automatic ones are kept for the last 200 saves.");
        if (label == null) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("file.checkpoint", new JsonObject { ["fileId"] = ActiveTab.FileId, ["label"] = label })))
        {
            Services.Notifier.Success($"Saved version \"{label}\".");
            await LoadVersionsAsync();
        }
    }

    [RelayCommand]
    private async Task RestoreVersionAsync(VersionItem? version)
    {
        version ??= SelectedVersion;
        if (version == null || ActiveTab == null) return;
        if (!await Services.Dialogs.ConfirmAsync("Restore version",
                $"Replace {ActiveTab.Title} with the version from {Formatting.MessageTime(version.Time)}? Everyone editing it sees the change. The current text is saved as a version first.",
                "Restore")) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("file.restore", new JsonObject { ["versionId"] = version.Id })))
        {
            Services.Notifier.Success("Version restored.");
            await LoadVersionsAsync();
        }
    }

    // ---- running code ------------------------------------------------------------------------

    [ObservableProperty] private bool _isRunPanelOpen;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCodeCommand), nameof(StopRunCommand))] private bool _isRunning;
    [ObservableProperty] private string _runStatus = "";
    [ObservableProperty] private string _stdin = "";
    public string? RunId { get; private set; }
    public event Action<string, string>? OutputAppended; // stream, text
    public event Action? OutputCleared;

    public bool CanRunActive => ActiveTab != null && CanEdit && !IsRunning
        && Path.GetExtension(ActiveTab.Path).ToLowerInvariant() is ".py" or ".js" or ".c" or ".cpp" or ".cc" or ".java";

    [RelayCommand(CanExecute = nameof(CanRunActive))]
    private async Task RunCodeAsync()
    {
        if (ActiveTab == null) return;
        IsRunPanelOpen = true;
        OutputCleared?.Invoke();
        RunStatus = $"Running {ActiveTab.Title}...";
        IsRunning = true;
        try
        {
            var reply = await Services.Connection.RequestAsync("run.start", new JsonObject { ["fileId"] = ActiveTab.FileId, ["stdin"] = Stdin });
            RunId = reply.Str("runId");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            RunStatus = Describe(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void StopRun()
    {
        if (RunId != null) Services.Connection.Send("run.stop", new JsonObject { ["runId"] = RunId });
        RunStatus = "Stopping...";
    }

    [RelayCommand] private void ToggleRunPanel() => IsRunPanelOpen = !IsRunPanelOpen;

    private void AppendOutput(string stream, string text) => OutputAppended?.Invoke(stream, text);

    private void RunFinished(JsonObject e)
    {
        IsRunning = false;
        RunId = null;
        double seconds = e.Long("durationMs") / 1000.0;
        string error = e.Str("error");
        RunStatus = error.Length > 0 ? error : $"Finished with exit code {e.Long("exitCode")} in {seconds:0.0} s.";
        RunCodeCommand.NotifyCanExecuteChanged();
    }
}
