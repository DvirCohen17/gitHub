using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

// A file or folder in the project tree.
public sealed partial class TreeNode : ObservableObject
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsFolder { get; init; }
    public long FileId { get; init; }
    public ObservableCollection<TreeNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    // Who is editing this file right now (from presence).
    [ObservableProperty] private string _editors = "";

    public string Glyph => IsFolder ? (IsExpanded ? "" : "") : FileGlyph(Name);

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Glyph));

    private static string FileGlyph(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".md" or ".txt" => "",
        ".json" or ".xml" or ".yml" or ".yaml" => "",
        _ => "",
    };

    // Builds the tree from the server's flat lists; keeps folders open that were open.
    public static List<TreeNode> Build(IEnumerable<(long Id, string Path)> files, IEnumerable<string> folders, ISet<string> expanded)
    {
        var roots = new List<TreeNode>();
        var folderNodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);

        TreeNode Folder(string path)
        {
            if (folderNodes.TryGetValue(path, out var existing)) return existing;
            int slash = path.LastIndexOf('/');
            var node = new TreeNode { Name = slash < 0 ? path : path[(slash + 1)..], Path = path, IsFolder = true, IsExpanded = expanded.Contains(path) };
            folderNodes[path] = node;
            (slash < 0 ? (IList<TreeNode>)roots : Folder(path[..slash]).Children).Add(node);
            return node;
        }

        foreach (string folder in folders) Folder(folder);
        foreach (var (id, path) in files)
        {
            int slash = path.LastIndexOf('/');
            var node = new TreeNode { Name = slash < 0 ? path : path[(slash + 1)..], Path = path, FileId = id };
            (slash < 0 ? (IList<TreeNode>)roots : Folder(path[..slash]).Children).Add(node);
        }
        Sort(roots);
        return roots;
    }

    private static void Sort(IList<TreeNode> nodes)
    {
        var sorted = nodes.OrderBy(n => n.IsFolder ? 0 : 1).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        nodes.Clear();
        foreach (var n in sorted)
        {
            Sort(n.Children);
            nodes.Add(n);
        }
    }

    public IEnumerable<TreeNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants()) yield return d;
        }
    }
}

// An open file in the editor area.
public sealed partial class EditorTab : ObservableObject
{
    public EditorTab(LiveDocument document)
    {
        Document = document;
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveDocument.Path)) OnPropertyChanged(nameof(Title));
        };
    }

    public LiveDocument Document { get; }
    public long FileId => Document.FileId;
    public string Title => System.IO.Path.GetFileName(Document.Path);
    public string Path => Document.Path;

    [ObservableProperty] private bool _isActive;
}

public sealed partial class MemberItem : ObservableObject
{
    public required UserRef User { get; init; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    private string _role = "member";
    public string RoleLabel => Role switch { "owner" => "Owner", "admin" => "Admin", _ => "Member" };
}

// One chat message. Text is split into plain parts and ``` code blocks by the view.
public sealed partial class ChatMessageItem : ObservableObject
{
    public long Id { get; init; }
    public required UserRef User { get; init; }
    public DateTime Time { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleted))]
    private string _text = "";

    [ObservableProperty] private bool _edited;
    [ObservableProperty] private bool _deleted;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editDraft = "";
    [ObservableProperty] private bool _showHeader = true; // false when it continues the previous person's messages

    public bool IsDeleted => Deleted;
    public bool IsMine => User.Id == Services.Session.Me?.Id;
    public string TimeText => Formatting.MessageTime(Time);
    public bool MentionsMe => !IsMine && Services.Session.Me != null
        && Text.Contains("@" + Services.Session.Me.Name, StringComparison.OrdinalIgnoreCase);

    public static ChatMessageItem From(JsonObject m) => new()
    {
        Id = m.Long("id"),
        User = Users.From(m.Obj("user")),
        Time = m.Time("createdAt"),
        Text = m.Str("text"),
        Edited = m["editedAt"] != null && !m.Bool("deleted"),
        Deleted = m.Bool("deleted"),
    };

    public void Update(JsonObject m)
    {
        Text = m.Str("text");
        Deleted = m.Bool("deleted");
        Edited = m["editedAt"] != null && !Deleted;
    }
}

public sealed class VersionItem
{
    public long Id { get; init; }
    public string Label { get; init; } = "";
    public DateTime Time { get; init; }
    public UserRef? User { get; init; }
    public long Size { get; init; }
    public bool IsNamed => Label.Length > 0 && Label != "Before restore";
    public string Heading => Label.Length > 0 ? Label : "Saved automatically";
    public string Details => $"{Formatting.MessageTime(Time)}{(User != null ? " · " + User.Name : "")} · {Formatting.Size(Size)}";
}
