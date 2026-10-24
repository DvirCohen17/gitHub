using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;

namespace GitHubDemo.ViewModels;

public sealed class ThemeChoice
{
    public required string Name { get; init; }
    public required Brush Background { get; init; }
    public required Brush Foreground { get; init; }
    public required Brush Accent { get; init; }
    public bool IsSelected { get; set; }
}

// One <Color name=... foreground=...> entry of a colour (.xshd) file, edited in place.
public sealed class ColorEntry : INotifyPropertyChanged
{
    public static readonly List<string> ColorNames = typeof(Colors).GetProperties().Select(p => p.Name).OrderBy(n => n).ToList();
    private readonly XElement _element;

    public ColorEntry(XElement element)
    {
        _element = element;
        string current = element.Attribute("foreground")?.Value ?? "";
        Choices = ColorNames.Contains(current, StringComparer.OrdinalIgnoreCase) || current.Length == 0
            ? ColorNames
            : new List<string> { current }.Concat(ColorNames).ToList(); // keep custom values like #FF8000
    }

    public string Name => _element.Attribute("name")?.Value ?? "";
    public List<string> Choices { get; }

    public string ColorName
    {
        get
        {
            string value = _element.Attribute("foreground")?.Value ?? "";
            return Choices.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)) ?? value;
        }
        set
        {
            if (value == null || string.Equals(value, ColorName, StringComparison.OrdinalIgnoreCase)) return;
            _element.SetAttributeValue("foreground", value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorName)));
        }
    }

    public Brush Brush => Converters.ColorNameToBrush(ColorName);
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class SettingsViewModel : PageViewModel
{
    private static readonly XNamespace Xshd = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";
    private XDocument? _styleDocument;
    private string _stylePath = "";
    private bool _loadingLanguage;

    public SettingsViewModel(string section = "profile")
    {
        _section = section;
        _fontSize = Services.Settings.EditorFontSize;
        _wordWrap = Services.Settings.EditorWordWrap;
        _notifications = Services.Settings.Notifications;
        _notifyChat = Services.Settings.NotifyChat;
        _bio = Services.Session.Bio;
        foreach (var name in AvatarStore.Presets)
            Presets.Add(new PresetAvatar { Key = name, Image = AvatarStore.Preset(name) });
        RefreshThemes();
    }

    public override string Title => "Settings";
    public override string NavKey => "settings";
    public Session Session => Services.Session;

    [ObservableProperty] private string _section;

    // ---- profile ---------------------------------------------------------------------

    public sealed class PresetAvatar
    {
        public required string Key { get; init; }
        public ImageSource? Image { get; init; }
    }

    public ObservableCollection<PresetAvatar> Presets { get; } = new();
    [ObservableProperty] private string _bio;
    [ObservableProperty] private bool _isCropping;
    [ObservableProperty] private BitmapSource? _cropSource;

    [RelayCommand]
    private async Task ChoosePresetAsync(PresetAvatar? preset)
    {
        if (preset == null) return;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("user.setAvatar", new JsonObject { ["preset"] = preset.Key });
            Services.Session.Me!.Avatar = reply.Str("avatar");
            Services.Notifier.Success("Profile picture updated.");
        });
    }

    [RelayCommand]
    private void UploadPicture()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Choose a picture" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            image.Freeze();
            if (image.PixelWidth < 32 || image.PixelHeight < 32)
            {
                Services.Notifier.Error("That picture is too small (at least 32x32 pixels).");
                return;
            }
            CropSource = image;
            IsCropping = true;
        }
        catch (Exception)
        {
            Services.Notifier.Error("That file isn't a picture gitHub Demo can read.");
        }
    }

    [RelayCommand] private void CancelCrop() => IsCropping = false;

    // Called by the cropper with the finished 256x256 PNG.
    public async Task SaveCroppedAsync(byte[] png)
    {
        if (await TryAsync(async () =>
            {
                var reply = await Services.Connection.RequestAsync("user.setAvatar", new JsonObject { ["png"] = Convert.ToBase64String(png) }, 60000);
                Services.Session.Me!.Avatar = reply.Str("avatar");
            }))
        {
            IsCropping = false;
            Services.Notifier.Success("Profile picture updated.");
        }
    }

    [RelayCommand]
    private async Task RemovePictureAsync()
    {
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("user.setAvatar");
            Services.Session.Me!.Avatar = reply.Str("avatar");
        });
    }

    [RelayCommand]
    private async Task SaveBioAsync()
    {
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("user.updateProfile", new JsonObject { ["bio"] = Bio });
            Services.Session.Bio = reply.Str("bio");
            Services.Notifier.Success("Bio saved.");
        });
    }

    [RelayCommand] private void ViewProfile() { if (Services.Session.Me != null) Services.Main.OpenProfile(Services.Session.Me.Name); }

    // ---- appearance -----------------------------------------------------------------

    public ObservableCollection<ThemeChoice> Themes { get; } = new();

    private void RefreshThemes()
    {
        Themes.Clear();
        foreach (var p in ThemeManager.Palettes)
        {
            Themes.Add(new ThemeChoice
            {
                Name = p.Name, Background = ThemeManager.Solid(p.Background), Foreground = ThemeManager.Solid(p.Text),
                Accent = ThemeManager.Solid(p.Accent), IsSelected = p.Name == ThemeManager.Current.Name,
            });
        }
    }

    [RelayCommand]
    private void ChooseTheme(ThemeChoice? theme)
    {
        if (theme == null) return;
        ThemeManager.Apply(theme.Name);
        Services.Settings.Theme = theme.Name;
        Services.Settings.Save();
    }

    // ---- editor ------------------------------------------------------------------------

    [ObservableProperty] private double _fontSize;
    [ObservableProperty] private bool _wordWrap;
    public IReadOnlyList<string> Languages => CodeStyles.Languages;
    [ObservableProperty] private string? _language;
    [ObservableProperty] private string _previewFileName = "";
    [ObservableProperty] private string _previewCode = "";
    [ObservableProperty] private IHighlightingDefinition? _previewHighlighting;
    [ObservableProperty] private bool _unsavedColors;
    [ObservableProperty] private string _colorsHint = "";
    public ObservableCollection<ColorEntry> ColorEntries { get; } = new();

    partial void OnFontSizeChanged(double value)
    {
        Services.Settings.EditorFontSize = Math.Clamp(value, 9, 32);
        Services.Settings.Save();
    }

    partial void OnWordWrapChanged(bool value)
    {
        Services.Settings.EditorWordWrap = value;
        Services.Settings.Save();
    }

    partial void OnSectionChanged(string value)
    {
        if (value == "editor" && Language == null) Language = Languages[0];
    }

    public override Task LoadAsync()
    {
        if (Section == "editor" && Language == null) Language = Languages[0];
        return Task.CompletedTask;
    }

    partial void OnLanguageChanging(string? value)
    {
        _loadingLanguage = true;
    }

    partial void OnLanguageChanged(string? oldValue, string? newValue)
    {
        _loadingLanguage = false;
        if (newValue == null) return;
        if (UnsavedColors && oldValue != null)
        {
            _ = ConfirmLanguageSwitchAsync(oldValue, newValue);
            return;
        }
        LoadColors(newValue);
    }

    private async Task ConfirmLanguageSwitchAsync(string oldValue, string newValue)
    {
        if (await Services.Dialogs.ConfirmAsync("Unsaved colours", $"Discard your unsaved {oldValue} colours?", "Discard"))
        {
            UnsavedColors = false;
            LoadColors(newValue);
        }
        else
        {
            _language = oldValue; // put the selection back without reloading
            OnPropertyChanged(nameof(Language));
        }
    }

    private void LoadColors(string language)
    {
        var sample = SampleCode.For(language);
        PreviewFileName = sample.FileName;
        PreviewCode = sample.Code;
        ColorEntries.Clear();
        _styleDocument = null;
        UnsavedColors = false;
        _stylePath = CodeStyles.UserFile(language);
        if (!File.Exists(_stylePath))
        {
            ColorsHint = $"There's no colour file for {language}, so the editor uses built-in colours.";
            PreviewHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(sample.FileName));
            return;
        }
        try
        {
            _styleDocument = XDocument.Load(_stylePath);
            foreach (var element in _styleDocument.Descendants(Xshd + "Color").Where(c => c.Attribute("name") != null))
            {
                var entry = new ColorEntry(element);
                entry.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(ColorEntry.ColorName) || _loadingLanguage) return;
                    UnsavedColors = true;
                    RefreshPreview();
                };
                ColorEntries.Add(entry);
            }
            ColorsHint = "Pick a colour: the preview changes immediately. Save to use the colours in the editor.";
            RefreshPreview();
        }
        catch (Exception ex)
        {
            ColorsHint = $"Couldn't read the colour file: {ex.Message}";
        }
    }

    // Rebuilds the highlighting from the in-memory (unsaved) colour file.
    private void RefreshPreview()
    {
        if (_styleDocument == null) return;
        try
        {
            using XmlReader reader = _styleDocument.CreateReader();
            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            if (ThemeManager.Current.IsDark) CodeStyles.AdaptToDarkBackground(definition); // as the editor shows it
            PreviewHighlighting = definition;
        }
        catch (Exception ex)
        {
            ColorsHint = $"Preview unavailable: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveColors()
    {
        if (_styleDocument == null) return;
        try
        {
            _styleDocument.Save(_stylePath);
            UnsavedColors = false;
            CodeStyles.Invalidate();
            Services.Notifier.Success("Colours saved. Open editors use them right away.");
        }
        catch (Exception ex)
        {
            Services.Notifier.Error($"Couldn't save the colours: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetColors()
    {
        if (Language == null) return;
        try
        {
            File.Delete(_stylePath); // the bundled file is copied again
            CodeStyles.Invalidate();
            LoadColors(Language);
            Services.Notifier.Info($"{Language} colours reset to the defaults.");
        }
        catch (Exception ex)
        {
            Services.Notifier.Error(ex.Message);
        }
    }

    public override async Task<bool> CanLeaveAsync() =>
        !UnsavedColors || await Services.Dialogs.ConfirmAsync("Unsaved colours", "You changed editor colours without saving. Leave anyway?", "Leave");

    // ---- notifications ------------------------------------------------------------------

    [ObservableProperty] private bool _notifications;
    [ObservableProperty] private bool _notifyChat;

    partial void OnNotificationsChanged(bool value)
    {
        Services.Settings.Notifications = value;
        Services.Settings.Save();
    }

    partial void OnNotifyChatChanged(bool value)
    {
        Services.Settings.NotifyChat = value;
        Services.Settings.Save();
    }

    [RelayCommand] private void TestNotification() =>
        Services.Notifier.Show(new ToastItem { Title = "Notifications are on", Message = "You'll see invites, mentions and tasks here.", Kind = "message", User = Services.Session.Me });

    // ---- about ---------------------------------------------------------------------------

    public string ClientVersion => Session.ClientVersion;
    public string ServerText => $"{Services.Connection.Host}:{Services.Connection.Port} (server {Services.Session.ServerVersion})";
    public string DataFolder => AppSettings.DataDir;

    [RelayCommand] private void OpenDataFolder() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppSettings.DataDir) { UseShellExecute = true });
}

// Sample programs for the colour preview: comments, strings, numbers, keywords...
public static class SampleCode
{
    public static (string FileName, string Code) For(string language) => language switch
    {
        "C#" => ("Program.cs", """
            using System;

            namespace Demo
            {
                // Greets everyone in the project
                public class Greeter
                {
                    private const int Count = 3;

                    public static void Main(string[] args)
                    {
                        string name = args.Length > 0 ? args[0] : "world";
                        for (int i = 0; i < Count; i++)
                        {
                            Console.WriteLine($"Hello, {name}! #{i + 1}");
                        }
                    }
                }
            }
            """),
        "Python" => ("main.py", """
            import math

            # Greets everyone in the project
            class Greeter:
                def __init__(self, name="world"):
                    self.name = name

                def greet(self, times=3):
                    for i in range(times):
                        print(f"Hello, {self.name}! #{i + 1}")
                    return math.pi * 2


            if __name__ == "__main__":
                Greeter("team").greet()
            """),
        "Java" => ("Main.java", """
            import java.util.List;

            // Greets everyone in the project
            public class Main {
                private static final int COUNT = 3;

                public static void main(String[] args) {
                    String name = args.length > 0 ? args[0] : "world";
                    for (int i = 0; i < COUNT; i++) {
                        System.out.println("Hello, " + name + "! #" + (i + 1));
                    }
                    List<Character> letters = List.of('a', 'b');
                }
            }
            """),
        "JavaScript" => ("app.js", """
            // Greets everyone in the project
            const COUNT = 3;

            class Greeter {
                constructor(name = "world") {
                    this.name = name;
                }

                greet() {
                    for (let i = 0; i < COUNT; i++) {
                        console.log(`Hello, ${this.name}! #${i + 1}`);
                    }
                    return this.name.length * 2.5;
                }
            }

            new Greeter("team").greet();
            """),
        _ => ("main.cpp", """
            #include <iostream>
            #include <string>

            // Greets everyone in the project
            namespace demo {
                const int COUNT = 3;

                class Greeter {
                public:
                    explicit Greeter(std::string name) : name_(std::move(name)) {}

                    void greet() const {
                        for (int i = 0; i < COUNT; ++i) {
                            std::cout << "Hello, " << name_ << "! #" << i + 1 << '\n';
                        }
                    }

                private:
                    std::string name_;
                };
            }

            int main() {
                demo::Greeter("team").greet();
                return 0;
            }
            """),
    };
}
