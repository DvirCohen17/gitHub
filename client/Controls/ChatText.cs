using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GitHubDemo.Core;
using ICSharpCode.AvalonEdit;

namespace GitHubDemo.Controls;

// A chat message: plain text with @mentions highlighted, and ```lang fenced
// code blocks shown with syntax colouring and a copy button.
public sealed class ChatText : StackPanel
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ChatText), new PropertyMetadata("", (d, _) => ((ChatText)d).Build()));

    private static readonly Regex Fence = new(@"```([A-Za-z0-9#+]*)[ \t]*\r?\n?(.*?)```", RegexOptions.Singleline);
    private static readonly Regex Mention = new(@"(?<![A-Za-z0-9.])@([A-Za-z0-9._-]{3,20})");

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Build()
    {
        Children.Clear();
        string text = Text ?? "";
        int pos = 0;
        foreach (Match m in Fence.Matches(text))
        {
            if (m.Index > pos) AddText(text.Substring(pos, m.Index - pos).Trim('\r', '\n'));
            AddCode(m.Groups[1].Value, m.Groups[2].Value.TrimEnd('\r', '\n'));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) AddText(text.Substring(pos).Trim('\r', '\n'));
    }

    private void AddText(string text)
    {
        if (text.Length == 0) return;
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Foreground");
        int pos = 0;
        string me = Services.Session.Me?.Name ?? "";
        foreach (Match m in Mention.Matches(text))
        {
            if (m.Index > pos) block.Inlines.Add(new Run(text.Substring(pos, m.Index - pos)));
            var run = new Run(m.Value) { FontWeight = FontWeights.SemiBold };
            if (string.Equals(m.Groups[1].Value, me, StringComparison.OrdinalIgnoreCase))
            {
                run.SetResourceReference(TextElement.BackgroundProperty, "Theme.AccentSoft");
                run.SetResourceReference(TextElement.ForegroundProperty, "Theme.Foreground");
            }
            else
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "Theme.AccentText");
            }
            block.Inlines.Add(run);
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) block.Inlines.Add(new Run(text.Substring(pos)));
        Children.Add(block);
    }

    private void AddCode(string language, string code)
    {
        var editor = new TextEditor
        {
            Text = code,
            IsReadOnly = true,
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
            FontSize = 12.5,
            SyntaxHighlighting = CodeStyles.ForFence(language),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 280,
            Padding = new Thickness(10, 8, 10, 8),
            Background = Brushes.Transparent,
        };
        editor.SetResourceReference(Control.ForegroundProperty, "Theme.Foreground");
        editor.TextArea.Caret.CaretBrush = Brushes.Transparent;

        var copy = new Button { Content = "", ToolTip = "Copy code", Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 4, 0) };
        copy.SetResourceReference(StyleProperty, "IconButton");
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(code);
            Services.Notifier.Success("Code copied.");
        };

        var header = new TextBlock { Text = language.Length > 0 ? language : "code", FontSize = 11, Margin = new Thickness(10, 6, 0, 0) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Muted");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(header);
        grid.Children.Add(copy);
        Grid.SetRowSpan(copy, 2);
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);

        var border = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 4), Child = grid };
        border.SetResourceReference(Border.BackgroundProperty, "Theme.EditorBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        Children.Add(border);
    }
}
