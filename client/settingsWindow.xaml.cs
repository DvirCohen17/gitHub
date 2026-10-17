using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace client_side
{
    // Theme (remembered for next time) and editor syntax colours with a live
    // preview: every colour change is applied to the sample code immediately;
    // "Save colours" writes the .xshd file the editor loads (see SyntaxStyles).
    public partial class settingsWindow : Window
    {
        private static readonly string[] Themes =
            { "Dark", "Light", "Blue", "Green", "Red", "CyberPunk", "Matrix", "Solarized Light", "Solarized Dark", "Vintage", "Neon", "Pastel" };
        private static readonly XNamespace Xshd = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";
        private static readonly List<string> ColorNames = typeof(Colors).GetProperties().Select(p => p.Name).OrderBy(n => n).ToList();

        private readonly Communicator communicator;
        private XDocument styleDocument;
        private string stylePath;
        private bool loadingThemes;
        private bool unsavedColors;
        private volatile bool isListeningToServer = true;
        private bool disconnect = true;

        public settingsWindow(Communicator communicator)
        {
            InitializeComponent();
            MainFrame.Attach(this); // same size and place as the previous screen
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            languageComboBox.ItemsSource = SyntaxStyles.Languages;
            previewEditor.SetResourceReference(ForegroundProperty, "Theme.Foreground");
            previewEditor.SetResourceReference(ICSharpCode.AvalonEdit.TextEditor.LineNumbersForegroundProperty, "Theme.Muted");
            communicator.ApplyTheme(this);
            LoadThemes();
            Closing += (_, e) =>
            {
                if (disconnect && !ConfirmDiscardColors())
                {
                    e.Cancel = true;
                    return;
                }
                isListeningToServer = false;
                if (disconnect)
                {
                    try { communicator.SendData($"{(int)MessageCodes.MC_DISCONNECT}{communicator.UserId:D5}"); } catch (Exception) { }
                }
            };
            new System.Threading.Thread(ReceiveServerUpdates) { IsBackground = true }.Start();
        }

        private bool ConfirmDiscardColors() => !unsavedColors ||
            MessageBox.Show(this, "You changed editor colours without saving. Leave anyway?", "Unsaved colours",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        // Only "back to home" (240) matters here; heartbeats are ignored.
        private void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    string update = communicator.ReceiveData();
                    if (update.StartsWith("240"))
                    {
                        Dispatcher.Invoke(GoHome);
                    }
                }
            }
            catch (Exception ex)
            {
                if (isListeningToServer) communicator.HandleListenerError(ex);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmDiscardColors())
            {
                communicator.SendData($"{(int)MessageCodes.MC_BACK_TO_HOME_PAGE_REQUEST}");
            }
        }

        private void GoHome()
        {
            disconnect = false;
            isListeningToServer = false;
            new HomePage(communicator).Show();
            Close();
        }

        private void SetStatus(string text, bool isError = false)
        {
            txtStatus.Text = text;
            txtStatus.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Theme.Danger" : "Theme.Muted");
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (themePanel == null) return; // during InitializeComponent
            bool colors = colorsTab.IsChecked == true;
            themePanel.Visibility = colors ? Visibility.Collapsed : Visibility.Visible;
            colorsPanel.Visibility = colors ? Visibility.Visible : Visibility.Collapsed;
            saveColorsBtn.Visibility = colors ? Visibility.Visible : Visibility.Collapsed;
            if (colors && languageComboBox.SelectedIndex < 0)
            {
                languageComboBox.SelectedIndex = 0;
            }
        }

        // ---- theme

        private void LoadThemes()
        {
            loadingThemes = true;
            string current = communicator.AppTheme?.theame ?? "Dark";
            themeList.ItemsSource = Themes.Select(name =>
            {
                Theme theme = Communicator.CreateTheme(name); // colours only: nothing applied or saved
                return new ThemePreview
                {
                    Name = name,
                    Background = theme.Background,
                    Foreground = theme.TextColor,
                    Accent = Communicator.AccentBrushFor(name),
                    IsSelected = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                };
            }).ToList();
            loadingThemes = false;
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (loadingThemes || (sender as FrameworkElement)?.Tag is not string theme)
            {
                return;
            }
            communicator.SaveTheme(theme);
            SetStatus($"Theme set to {theme}.");
        }

        // ---- editor colours

        private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (languageComboBox.SelectedItem is not string language)
            {
                return;
            }
            if (unsavedColors && MessageBox.Show(this, "Discard your unsaved colour changes?", "Unsaved colours",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                languageComboBox.SelectionChanged -= Language_SelectionChanged;
                languageComboBox.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
                languageComboBox.SelectionChanged += Language_SelectionChanged;
                return;
            }
            unsavedColors = false;

            var sample = SampleCode.For(language);
            txtPreviewFile.Text = sample.FileName;
            previewEditor.Text = sample.Code;

            stylePath = SyntaxStyles.StyleFile(communicator.CodeStylesDir, language);
            colorList.ItemsSource = null;
            styleDocument = null;
            if (!File.Exists(stylePath))
            {
                txtColorsHint.Text = $"No colour file is installed for {language}, so the editor uses its built-in colours (shown in the preview).";
                previewEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(sample.FileName));
                saveColorsBtn.IsEnabled = false;
                return;
            }
            try
            {
                styleDocument = XDocument.Load(stylePath);
                var entries = styleDocument.Descendants(Xshd + "Color")
                    .Where(c => c.Attribute("name") != null)
                    .Select(c => new ColorEntry(c))
                    .ToList();
                foreach (var entry in entries)
                {
                    entry.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(ColorEntry.ColorName))
                        {
                            unsavedColors = true;
                            RefreshPreview();
                            SetStatus("Unsaved changes - press Save colours to keep them.");
                        }
                    };
                }
                colorList.ItemsSource = entries;
                txtColorsHint.Text = "Pick a colour and the preview updates instantly. Files you open after saving use the new colours.";
                saveColorsBtn.IsEnabled = true;
                RefreshPreview();
            }
            catch (Exception ex)
            {
                txtColorsHint.Text = $"Couldn't read {stylePath}: {ex.Message}";
                saveColorsBtn.IsEnabled = false;
            }
        }

        // Rebuilds the highlighting from the (unsaved) in-memory colour file.
        private void RefreshPreview()
        {
            if (styleDocument == null) return;
            try
            {
                using XmlReader reader = styleDocument.CreateReader();
                previewEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch (Exception ex)
            {
                SetStatus($"Preview unavailable: {ex.Message}", true);
            }
        }

        private void SaveColors_Click(object sender, RoutedEventArgs e)
        {
            if (styleDocument == null)
            {
                return;
            }
            try
            {
                styleDocument.Save(stylePath);
                unsavedColors = false;
                SetStatus("Colours saved. They apply to files you open from now on.");
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't save colours: {ex.Message}", true);
            }
        }


        public class ThemePreview
        {
            public string Name { get; set; }
            public Brush Background { get; set; }
            public Brush Foreground { get; set; }
            public Brush Accent { get; set; }
            public bool IsSelected { get; set; }
        }

        // One <Color name=... foreground=...> entry of the .xshd file, edited in place.
        public class ColorEntry : INotifyPropertyChanged
        {
            private readonly XElement element;

            public ColorEntry(XElement element)
            {
                this.element = element;
                string current = element.Attribute("foreground")?.Value ?? "";
                // Keep custom values like "#FF8000" selectable too.
                Choices = ColorNames.Contains(current, StringComparer.OrdinalIgnoreCase) || current.Length == 0
                    ? ColorNames
                    : new List<string> { current }.Concat(ColorNames).ToList();
            }

            public string Name => element.Attribute("name")?.Value;
            public List<string> Choices { get; }

            public string ColorName
            {
                get
                {
                    string value = element.Attribute("foreground")?.Value ?? "";
                    return Choices.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)) ?? value;
                }
                set
                {
                    if (value == null || string.Equals(value, ColorName, StringComparison.OrdinalIgnoreCase)) return;
                    element.SetAttributeValue("foreground", value);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorName)));
                }
            }

            public Brush Brush => ColorNameToBrushConverter.ToBrush(ColorName);

            public event PropertyChangedEventHandler PropertyChanged;
        }
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
}
