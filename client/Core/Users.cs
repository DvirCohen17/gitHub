using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubDemo.Core;

// One shared object per user. Lists everywhere bind to the same instance, so a
// new picture or presence change shows up on every screen at once.
public sealed partial class UserRef : ObservableObject
{
    public UserRef(long id, string name)
    {
        Id = id;
        _name = name;
    }

    public long Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initials), nameof(Color))]
    private string _name;

    [ObservableProperty]
    private string _avatar = "";

    [ObservableProperty]
    private ImageSource? _picture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(PresenceText), nameof(PresenceBrushKey))]
    private string _status = "offline";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresenceText))]
    private string _projectName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresenceText))]
    private string _file = "";

    [ObservableProperty]
    private long _projectId;

    public bool IsMe => Id == Services.Session.Me?.Id;
    public bool IsOnline => Status != "offline";
    public string Initials => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
    public Brush Color => ThemeManager.Solid(ThemeManager.UserColor(Name));
    public string PresenceBrushKey => Status switch { "online" => "Theme.Online", "away" => "Theme.Away", _ => "Theme.Offline" };

    public string PresenceText
    {
        get
        {
            if (Status == "offline") return "Offline";
            string state = Status == "away" ? "Away" : "Online";
            if (!string.IsNullOrEmpty(File)) return $"{state} · editing {System.IO.Path.GetFileName(File)}";
            if (!string.IsNullOrEmpty(ProjectName)) return $"{state} · in {ProjectName}";
            return state;
        }
    }

    partial void OnAvatarChanged(string value) => AvatarStore.Load(this);
}

public static class Users
{
    private static readonly Dictionary<long, UserRef> All = new();

    // {"id", "name", "avatar"} -> the shared UserRef, updated with these values.
    public static UserRef From(JsonObject? json)
    {
        long id = json.Long("id");
        if (!All.TryGetValue(id, out var user))
        {
            user = new UserRef(id, json.Str("name"));
            All[id] = user;
            user.Avatar = json.Str("avatar"); // loads the picture
        }
        else
        {
            string name = json.Str("name");
            if (name.Length > 0) user.Name = name;
            if (json?["avatar"] != null) user.Avatar = json.Str("avatar");
        }
        if (json.Obj("presence") is { } presence) ApplyPresence(user, presence);
        return user;
    }

    public static UserRef? Find(long id) => All.TryGetValue(id, out var u) ? u : null;

    public static void ApplyPresence(UserRef user, JsonObject presence)
    {
        user.Status = presence.Str("status", "offline");
        user.ProjectId = presence.Long("projectId");
        user.ProjectName = presence.Str("projectName");
        user.File = presence.Str("file");
    }

    public static void ApplyPresence(long id, JsonObject presence)
    {
        if (All.TryGetValue(id, out var user)) ApplyPresence(user, presence);
    }

    public static void Clear() => All.Clear();
}

// Profile pictures: presets are vector drawings in Themes/Avatars.xaml; uploaded
// pictures are fetched once and cached on disk by their hash.
public static class AvatarStore
{
    public static readonly string[] Presets =
    {
        "fox", "cat", "dog", "panda", "koala", "tiger", "lion", "frog",
        "monkey", "penguin", "owl", "octopus", "unicorn", "bear", "rabbit", "robot",
    };

    private static readonly Dictionary<string, ImageSource> Cache = new();
    private static readonly Dictionary<string, List<UserRef>> Waiting = new();

    public static ImageSource? Preset(string name) =>
        Application.Current.TryFindResource("Avatar." + name) as ImageSource;

    public static void Load(UserRef user)
    {
        string key = user.Avatar;
        if (key.StartsWith("p:"))
        {
            user.Picture = Preset(key.Substring(2));
        }
        else if (key.StartsWith("u:") && key.Length > 2)
        {
            string hash = key.Substring(2);
            if (Cache.TryGetValue(hash, out var image))
            {
                user.Picture = image;
                return;
            }
            string file = Path.Combine(AppSettings.AvatarCacheDir, hash + ".png");
            if (File.Exists(file) && FromFile(file) is { } cached)
            {
                Cache[hash] = cached;
                user.Picture = cached;
                return;
            }
            user.Picture = null; // initials until it arrives
            if (Waiting.TryGetValue(hash, out var list))
            {
                if (!list.Contains(user)) list.Add(user);
                return;
            }
            Waiting[hash] = new List<UserRef> { user };
            _ = FetchAsync(hash);
        }
        else
        {
            user.Picture = null;
        }
    }

    private static async Task FetchAsync(string hash)
    {
        try
        {
            var reply = await Services.Connection.RequestAsync("user.avatar", new JsonObject { ["hash"] = hash });
            byte[] png = Convert.FromBase64String(reply.Str("png"));
            Directory.CreateDirectory(AppSettings.AvatarCacheDir);
            string file = Path.Combine(AppSettings.AvatarCacheDir, hash + ".png");
            await File.WriteAllBytesAsync(file, png);
            if (FromFile(file) is { } image)
            {
                Cache[hash] = image;
                foreach (var user in Waiting.GetValueOrDefault(hash) ?? new())
                    if (user.Avatar == "u:" + hash) user.Picture = image;
            }
        }
        catch (Exception)
        {
            // keep showing initials
        }
        finally
        {
            Waiting.Remove(hash);
        }
    }

    public static BitmapImage? FromFile(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
