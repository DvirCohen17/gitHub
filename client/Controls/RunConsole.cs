using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GitHubDemo.Controls;

// Program output: stdout in the normal colour, stderr in red, messages from the
// server (e.g. "preparing the image") dimmed.
public sealed class RunConsole : RichTextBox
{
    private readonly Paragraph _paragraph = new() { Margin = new Thickness(0) };

    public RunConsole()
    {
        IsReadOnly = true;
        IsDocumentEnabled = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(10, 8, 10, 8);
        FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono");
        FontSize = 12.5;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Document = new FlowDocument(_paragraph) { PageWidth = 4000, PagePadding = new Thickness(0) };
        SetResourceReference(ForegroundProperty, "Theme.Foreground");
        AutomationProperties.SetAutomationId(this, "RunOutput");
    }

    public void Append(string stream, string text)
    {
        var run = new Run(text);
        if (stream == "stderr") run.SetResourceReference(TextElement.ForegroundProperty, "Theme.Danger");
        else if (stream == "system") run.SetResourceReference(TextElement.ForegroundProperty, "Theme.Muted");
        _paragraph.Inlines.Add(run);
        ScrollToEnd();
    }

    public void Clear() => _paragraph.Inlines.Clear();

    public string AllText => new TextRange(Document.ContentStart, Document.ContentEnd).Text;
}
