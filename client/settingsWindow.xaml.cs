using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using System.Windows.Media;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace client_side
{
    public partial class settingsWindow : Window
    {
        private Communicator communicator;
        private bool disconnect = true;
        private string theame;

        public settingsWindow(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(System.Windows.Window)); // Specified full namespace for Window
            this.communicator = communicator;
            communicator.ApplyTheme(this);
            theame = communicator.AppTheme.theame;
        }

        private void ModifyCodeStyle_Click(object sender, RoutedEventArgs e)
        {
            dynamicContentPanel.Children.Clear();

            TextBlock instructionText = new TextBlock
            {
                Text = "Select Code Language",
                Margin = new Thickness(5),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            dynamicContentPanel.Children.Add(instructionText);

            ComboBox codeLanguageComboBox = new ComboBox { Margin = new Thickness(5) };
            codeLanguageComboBox.Items.Add("C++");
            codeLanguageComboBox.Items.Add("C#");
            codeLanguageComboBox.Items.Add("Python");
            codeLanguageComboBox.Items.Add("Java");
            codeLanguageComboBox.Items.Add("JavaScript");
            codeLanguageComboBox.SelectionChanged += CodeLanguageComboBox_SelectionChanged;

            dynamicContentPanel.Children.Add(codeLanguageComboBox);
        }

        private void CodeLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            string selectedLanguage = comboBox.SelectedItem as string;
            string filePath = $"C:\\githubDemo\\codeStyles\\{selectedLanguage}-Mode.xshd";

            if (System.IO.File.Exists(filePath))
            {
                dynamicContentPanel.Children.Clear();
                dynamicContentPanel.Children.Add(comboBox);

                XDocument xml = XDocument.Load(filePath);
                XNamespace ns = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"; // Update this if your namespace is different

                // Set WrapPanel orientation to vertical
                WrapPanel wrapPanel = new WrapPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(5),
                    VerticalAlignment = VerticalAlignment.Top // Align to top for better layout
                };

                foreach (var colorElement in xml.Descendants(ns + "Color"))
                {
                    StackPanel colorPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(5),
                        Width = 400 // Set width to ensure horizontal spacing
                    };

                    TextBlock colorName = new TextBlock
                    {
                        Text = colorElement.Attribute("name")?.Value,
                        Width = 150, // Fixed width for alignment
                        TextWrapping = TextWrapping.Wrap
                    };

                    ComboBox colorPicker = new ComboBox
                    {
                        Width = 150, // Match width with TextBlock
                        Margin = new Thickness(5, 0, 0, 0) // Margin for spacing
                    };
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

                    wrapPanel.Children.Add(colorPanel);
                }

                // Add the WrapPanel to a ScrollViewer
                ScrollViewer scrollViewer = new ScrollViewer
                {
                    Content = wrapPanel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // Disable horizontal scroll if unnecessary
                    Margin = new Thickness(5) // Margin for ScrollViewer
                };

                dynamicContentPanel.Children.Add(scrollViewer);
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
                ApplyButton_Click(colorPicker, null);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            WrapPanel wrapPanel = dynamicContentPanel.Children.OfType<ScrollViewer>().Select(s => s.Content as WrapPanel).FirstOrDefault();

            if (wrapPanel == null)
            {
                MessageBox.Show("WrapPanel not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ComboBox languageSelector = dynamicContentPanel.Children.OfType<ComboBox>().FirstOrDefault();

            string selectedLanguage = languageSelector?.SelectedItem as string;
            string filePath = $"C:\\githubDemo\\codeStyles\\{selectedLanguage}-Mode.xshd";

            if (System.IO.File.Exists(filePath))
            {
                XDocument xml = XDocument.Load(filePath);
                XNamespace ns = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";

                XElement syntaxDefinition = xml.Root;
                if (syntaxDefinition != null)
                {
                    IEnumerable<XElement> colorElements = syntaxDefinition.Descendants(ns + "Color");
                    var colorElementDict = colorElements.ToDictionary(
                       elem => elem.Attribute("name")?.Value,
                       elem => elem
                   );

                    bool anyChangesMade = false;

                    foreach (StackPanel colorPanel in wrapPanel.Children.OfType<StackPanel>())
                    {
                        if (colorPanel.Children.Count >= 3 &&
                            colorPanel.Children[0] is TextBlock colorNameTextBlock &&
                            colorPanel.Children[1] is ComboBox colorPicker)
                        {
                            string colorName = colorNameTextBlock.Text;
                            string selectedColor = colorPicker.SelectedItem as string;

                            Debug.WriteLine($"Processing color panel: {colorName}, selected color: {selectedColor}");

                            if (colorElementDict.TryGetValue(colorName, out XElement colorElement))
                            {
                                colorElement.SetAttributeValue("foreground", selectedColor);
                                anyChangesMade = true;
                            }
                            else
                            {
                                Debug.WriteLine($"Color element '{colorName}' not found in XML.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Expected children not found in colorPanel.");
                        }
                    }

                    if (anyChangesMade)
                    {
                        try
                        {
                            xml.Save(filePath);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error saving XML file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show("No changes were made.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
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
            dynamicContentPanel.Children.Clear();

            List<string> themes = new List<string> { "Light", "Dark", "Blue", "Green", "Red", "CyberPunk", "Matrix" };

            foreach (var theme in themes)
            {
                CheckBox themeCheckBox = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = theme,
                        Foreground = communicator.AppTheme.TextColor
                    },
                    Margin = new Thickness(5)
                };

                if (theme == GetCurrentTheme())
                {
                    themeCheckBox.IsChecked = true;
                }

                themeCheckBox.Checked += ThemeCheckBox_Checked;

                dynamicContentPanel.Children.Add(themeCheckBox);
            }

        }

        private void ThemeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox selectedCheckBox = sender as CheckBox;
            string selectedTheme = null;

            if (selectedCheckBox.Content is TextBlock textBlock)
            {
                selectedTheme = textBlock.Text;
            }

            foreach (var child in dynamicContentPanel.Children)
            {
                if (child is CheckBox checkBox && checkBox != selectedCheckBox)
                {
                    if (checkBox.IsChecked == true)
                    {
                        checkBox.IsChecked = false;
                    }
                }
            }

            selectedCheckBox.IsChecked = true;

            // Apply the theme for the newly selected CheckBox
            SetTheme(selectedTheme);
        }

        private string GetCurrentTheme()
        {
            return communicator.AppTheme.theame;
        }

        private void SetTheme(string theme)
        {
            communicator.ModifyTheme(theme);
            communicator.ApplyTheme(this);

            foreach (var child in dynamicContentPanel.Children)
            {
                if (child is CheckBox checkBox )
                {
                    if (checkBox.Content is TextBlock textBlock)
                    {
                        textBlock.Foreground = communicator.AppTheme.TextColor;
                    }
                }
            }

            theame = communicator.AppTheme.theame;
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
