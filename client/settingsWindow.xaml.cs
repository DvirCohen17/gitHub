using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using System.Windows.Media;
using System.Linq;
using System;
using System.Collections.Generic;

namespace client_side
{
    public partial class settingsWindow : Window
    {
        private Communicator communicator;
        private bool disconnect = true;

        public settingsWindow(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(System.Windows.Window)); // Specified full namespace for Window
            this.communicator = communicator;
            communicator.ApplyTheme(this);
        }

        private void ModifyCodeStyle_Click(object sender, RoutedEventArgs e)
        {
            MainGrid.Children.Clear();

            StackPanel panel = new StackPanel { Orientation = Orientation.Vertical };
            ScrollViewer scrollViewer = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            ComboBox languageSelector = new ComboBox { Margin = new Thickness(5) };
            languageSelector.Items.Add("C++");
            languageSelector.Items.Add("C#");
            languageSelector.Items.Add("Python");
            languageSelector.Items.Add("Java");
            languageSelector.Items.Add("JavaScript");
            languageSelector.SelectionChanged += LanguageSelector_SelectionChanged;
            panel.Children.Add(languageSelector);

            MainGrid.Children.Add(scrollViewer);

            Button backButton = new Button { Content = "Back", Margin = new Thickness(5) };
            backButton.Click += BackButton_Click;
            panel.Children.Add(backButton);
            Button closeButton = new Button { Content = "Close Settings", Margin = new Thickness(5) };
            closeButton.Click += CloseButton_Click;
            panel.Children.Add(closeButton);
            communicator.ApplyTheme(this);
        }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            string selectedLanguage = comboBox.SelectedItem as string;
            string filePath = $"C:\\githubDemo\\codeStyles\\{selectedLanguage}-Mode.xshd";

            if (System.IO.File.Exists(filePath))
            {
                var scrollViewer = MainGrid.Children[0] as ScrollViewer;
                var panel = scrollViewer.Content as StackPanel;
                panel.Children.Clear();
                panel.Children.Add(comboBox);

                XDocument xml = XDocument.Load(filePath);
                XNamespace ns = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"; // Update this if your namespace is different

                foreach (var colorElement in xml.Descendants(ns + "Color"))
                {
                    StackPanel colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
                    TextBlock colorName = new TextBlock { Text = colorElement.Attribute("name")?.Value, Width = 150 };

                    ComboBox colorPicker = new ComboBox { Width = 100 };
                    var colorNames = typeof(Colors).GetProperties().Select(p => p.Name).ToList();
                    colorPicker.ItemsSource = colorNames;

                    string foregroundValue = colorElement.Attribute("foreground")?.Value;
                    colorPicker.SelectedItem = colorNames.FirstOrDefault(c => c.Equals(foregroundValue, StringComparison.InvariantCultureIgnoreCase));
                    colorPicker.Tag = colorElement;
                    colorPicker.SelectionChanged += ColorPicker_SelectionChanged;

                    TextBlock exampleText = new TextBlock
                    {
                        Text = "Example",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foregroundValue)),
                        Margin = new Thickness(10, 0, 0, 0),
                        Tag = colorPicker
                    };

                    colorPicker.Tag = exampleText; // Tag the ComboBox with the exampleText to update its color later

                    colorPanel.Children.Add(colorName);
                    colorPanel.Children.Add(colorPicker);
                    colorPanel.Children.Add(exampleText);

                    panel.Children.Add(colorPanel);
                }

                Button applyButton = new Button { Content = "Apply", Margin = new Thickness(5) };
                applyButton.Click += ApplyButton_Click;
                panel.Children.Add(applyButton);
                Button backButton = new Button { Content = "Back", Margin = new Thickness(5) };
                backButton.Click += BackButton_Click;
                panel.Children.Add(backButton);
                Button closeButton = new Button { Content = "Close Settings", Margin = new Thickness(5) };
                closeButton.Click += CloseButton_Click;
                panel.Children.Add(closeButton);

                communicator.ApplyTheme(this);

            }
            else
            {
                MessageBox.Show($"The file {filePath} does not exist.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox colorPicker = sender as ComboBox;
            TextBlock exampleText = colorPicker.Tag as TextBlock;
            string selectedColor = colorPicker.SelectedItem as string;

            if (exampleText != null && selectedColor != null)
            {
                exampleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedColor));
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = MainGrid.Children[0] as ScrollViewer;
            var panel = scrollViewer.Content as StackPanel;
            ComboBox languageSelector = panel.Children[0] as ComboBox;
            string selectedLanguage = languageSelector.SelectedItem as string;
            string filePath = $"C:\\githubDemo\\codeStyles\\{selectedLanguage}-Mode.xshd";

            // Load the XML document
            XDocument xml = XDocument.Load(filePath);
            XNamespace ns = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";

            // Find the SyntaxDefinition element
            XElement syntaxDefinition = xml.Root;

            if (syntaxDefinition != null)
            {
                // Find the Color elements under SyntaxDefinition
                IEnumerable<XElement> colorElements = syntaxDefinition.Descendants(ns + "Color");

                // Create a dictionary to map color names to their respective elements
                var colorElementDict = colorElements.ToDictionary(
                    elem => elem.Attribute("name")?.Value,
                    elem => elem
                );

                // Iterate through each StackPanel in the UI (skipping the first one for language selection)
                foreach (StackPanel colorPanel in panel.Children.OfType<StackPanel>().Skip(0))
                {
                    if (colorPanel.Children[0] is TextBlock colorNameTextBlock &&
                        colorPanel.Children[1] is ComboBox colorPicker)
                    {
                        string colorName = colorNameTextBlock.Text;
                        string selectedColor = colorPicker.SelectedItem as string;

                        // Check if the color element exists in the XML
                        if (colorElementDict.TryGetValue(colorName, out XElement colorElement))
                        {
                            colorElement.SetAttributeValue("foreground", selectedColor);
                        }
                    }
                }

                // Save the modified XML back to the file
                xml.Save(filePath);

                MessageBox.Show("Code style updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Error: SyntaxDefinition element not found in the XML file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            MainGrid.Children.Clear();
            var initialPanel = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10) };
            var modifyCodeStyleButton = new Button { Content = "Modify Code Style", Margin = new Thickness(5) };
            var modifyThemeButton = new Button { Content = "Modify Theme", Margin = new Thickness(5) };
            var closeSettingsButton = new Button { Content = "Close Settings", Margin = new Thickness(5) };
            modifyCodeStyleButton.Click += ModifyCodeStyle_Click;
            modifyThemeButton.Click += ModifyTheme_Click;
            closeSettingsButton.Click += CloseButton_Click;
            initialPanel.Children.Add(modifyCodeStyleButton);
            initialPanel.Children.Add(modifyThemeButton);
            initialPanel.Children.Add(closeSettingsButton);
            MainGrid.Children.Add(initialPanel);
            communicator.ApplyTheme(this);

        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            string code = ((int)MessageCodes.MC_BACK_TO_HOME_PAGE_REQUEST).ToString();
            communicator.SendData($"{code}");
            string msg = communicator.ReceiveData();
            disconnect = false;
            Dispatcher.Invoke(() =>
            {
                //AddProjectWindow addProjectWindow = new AddProjectWindow(communicator);
                HomePage addProjectWindow = new HomePage(communicator);
                addProjectWindow.Show();
                Close();
            });

        }

        private void ModifyTheme_Click(object sender, RoutedEventArgs e)
        {
            MainGrid.Children.Clear();

            StackPanel panel = new StackPanel { Orientation = Orientation.Vertical };
            ScrollViewer scrollViewer = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            ComboBox themeSelector = new ComboBox { Margin = new Thickness(5) };
            themeSelector.Items.Add("Light");
            themeSelector.Items.Add("Dark");
            themeSelector.Items.Add("Blue");
            themeSelector.Items.Add("Green");
            themeSelector.Items.Add("Red");
            themeSelector.Items.Add("CyberPunk");
            themeSelector.Items.Add("Matrix");
            themeSelector.SelectionChanged += ThemeSelector_SelectionChanged;
            panel.Children.Add(themeSelector);

            MainGrid.Children.Add(scrollViewer);

            Button backButton = new Button { Content = "Back", Margin = new Thickness(5) };
            backButton.Click += BackButton_Click;
            panel.Children.Add(backButton);
            Button closeButton = new Button { Content = "Close Settings", Margin = new Thickness(5) };
            closeButton.Click += CloseButton_Click;
            panel.Children.Add(closeButton);
            communicator.ApplyTheme(this);

        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            string selectedTheme = comboBox.SelectedItem as string;

            if (selectedTheme != null)
            {
                communicator.ModifyTheme(selectedTheme);
                communicator.ApplyTheme(this);
            }
        }

        private async void settings_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    string chatMessageCode = ((int)MessageCodes.MC_DISCONNECT).ToString();
                    string fullMessage = $"{chatMessageCode}{communicator.UserId:D5}";
                    communicator.SendData(fullMessage);
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
