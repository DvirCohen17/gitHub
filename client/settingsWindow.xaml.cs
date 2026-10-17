using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace client_side
{
    // Theme (saved for next time) and editor syntax colours (the same .xshd files
    // the editor loads, see SyntaxStyles).
    public partial class settingsWindow : Window, IServerMessageHandler
    {
        private static readonly string[] Themes =
            { "Dark", "Light", "Blue", "Green", "Red", "CyberPunk", "Matrix", "Solarized Light", "Solarized Dark", "Vintage", "Neon", "Pastel" };
        private static readonly XNamespace Xshd = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";
        private static readonly List<string> ColorNames = typeof(Colors).GetProperties().Select(p => p.Name).OrderBy(n => n).ToList();

        private readonly Communicator communicator;
        private XDocument styleDocument;
        private string stylePath;
        private bool loadingThemes;

        public settingsWindow(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            languageComboBox.ItemsSource = SyntaxStyles.Languages;
            communicator.ApplyTheme(this);
            LoadThemes();
        }

        public bool HandleServerMessage(string update) => false; // nothing server-side here

        private void SetStatus(string text, bool isError = false)
        {
            txtStatus.Text = text;
            txtStatus.Foreground = isError ? Brushes.IndianRed : (communicator.AppTheme?.TextColor ?? Brushes.White);
            txtStatus.Opacity = isError ? 1 : 0.65;
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
            var previews = new List<ThemePreview>();
            foreach (string name in Themes)
            {
                Theme theme = Communicator.CreateTheme(name); // colours only: nothing applied or saved
                previews.Add(new ThemePreview
                {
                    Name = name,
                    Background = theme.Background,
                    Foreground = theme.TextColor,
                    IsSelected = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                });
            }
            themeList.ItemsSource = previews;
            loadingThemes = false;
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (loadingThemes || (sender as FrameworkElement)?.Tag is not string theme)
            {
                return;
            }
            communicator.SaveTheme(theme);
            communicator.ApplyTheme(this);
            SetStatus($"Theme set to {theme}.");
        }

        // ---- editor colours

        private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (languageComboBox.SelectedItem is not string language)
            {
                return;
            }
            stylePath = SyntaxStyles.StyleFile(communicator.CodeStylesDir, language);
            colorList.ItemsSource = null;
            styleDocument = null;
            if (!File.Exists(stylePath))
            {
                txtColorsHint.Text = $"No colour file for {language} was installed ({stylePath}). The editor uses its built-in colours for it.";
                saveColorsBtn.IsEnabled = false;
                return;
            }
            try
            {
                styleDocument = XDocument.Load(stylePath);
                colorList.ItemsSource = styleDocument.Descendants(Xshd + "Color")
                    .Where(c => c.Attribute("name") != null)
                    .Select(c => new ColorEntry(c))
                    .ToList();
                txtColorsHint.Text = "Changes apply to files you open after saving.";
                saveColorsBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                txtColorsHint.Text = $"Couldn't read {stylePath}: {ex.Message}";
                saveColorsBtn.IsEnabled = false;
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
                SetStatus("Colours saved. Re-open a file to see them.");
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't save colours: {ex.Message}", true);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public class ThemePreview
        {
            public string Name { get; set; }
            public Brush Background { get; set; }
            public Brush Foreground { get; set; }
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
                    element.SetAttributeValue("foreground", value);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorName)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
                }
            }

            public Brush Brush
            {
                get
                {
                    try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorName)); }
                    catch { return Brushes.Gray; }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
