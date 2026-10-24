using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitHubDemo.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace GitHubDemo.Controls;

// The code editor for one open file: binds AvalonEdit to a LiveDocument, shows
// other people's cursors, and sends our caret position to them.
public sealed class EditorPane : ContentControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(LiveDocument), typeof(EditorPane), new PropertyMetadata(null, (d, e) => ((EditorPane)d).Attach(e.OldValue as LiveDocument, e.NewValue as LiveDocument)));

    private readonly TextEditor _editor;
    private readonly System.Windows.Threading.DispatcherTimer _flagTimer;
    private IBackgroundRenderer? _selections;
    private IBackgroundRenderer? _carets;

    public EditorPane()
    {
        Focusable = false;
        _editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
            Padding = new Thickness(6, 8, 6, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        AutomationProperties.SetAutomationId(_editor, "Editor");
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 4;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.SetResourceReference(BackgroundProperty, "Theme.EditorBackground");
        _editor.SetResourceReference(ForegroundProperty, "Theme.Foreground");
        _editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "Theme.Faint");
        _editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionBrushProperty, "Theme.EditorSelection");
        _editor.TextArea.SelectionBorder = null;
        _editor.TextArea.SelectionForeground = null;
        _editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
        _editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        _editor.TextArea.Caret.PositionChanged += (_, _) => ReportSelection();
        _editor.TextArea.SelectionChanged += (_, _) => ReportSelection();

        // Undo/redo go through the collaborative undo stack (only our own edits).
        _editor.TextArea.CommandBindings.Insert(0, new CommandBinding(ApplicationCommands.Undo,
            (_, e) => { Document?.Undo(); e.Handled = true; }, (_, e) => { e.CanExecute = Document?.CanUndo == true; e.Handled = true; }));
        _editor.TextArea.CommandBindings.Insert(0, new CommandBinding(ApplicationCommands.Redo,
            (_, e) => { Document?.Redo(); e.Handled = true; }, (_, e) => { e.CanExecute = Document?.CanRedo == true; e.Handled = true; }));

        // Ctrl + mouse wheel changes the font size (remembered).
        _editor.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Services.Settings.EditorFontSize = Math.Clamp(Services.Settings.EditorFontSize + (e.Delta > 0 ? 1 : -1), 9, 32);
            Services.Settings.Save();
            ApplySettings();
            e.Handled = true;
        };

        Content = _editor;
        // repaint once the name flags of other people's cursors have expired
        _flagTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(RemoteCaretRenderer.FlagSeconds + 0.2) };
        _flagTimer.Tick += (_, _) =>
        {
            _flagTimer.Stop();
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        };
        ApplySettings();
        CodeStyles.Changed += OnStylesChanged;
        Unloaded += (_, _) => CodeStyles.Changed -= OnStylesChanged;
        Loaded += (_, _) =>
        {
            CodeStyles.Changed -= OnStylesChanged;
            CodeStyles.Changed += OnStylesChanged;
            ApplySettings();
        };
    }

    public LiveDocument? Document
    {
        get => (LiveDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public TextEditor Editor => _editor;

    private void ApplySettings()
    {
        _editor.FontSize = Services.Settings.EditorFontSize;
        _editor.WordWrap = Services.Settings.EditorWordWrap;
    }

    private void OnStylesChanged()
    {
        if (Document != null) _editor.SyntaxHighlighting = CodeStyles.ForFile(Document.Path);
    }

    private void Attach(LiveDocument? old, LiveDocument? doc)
    {
        if (old != null)
        {
            old.CursorsChanged -= Redraw;
            old.PropertyChanged -= OnDocumentPropertyChanged;
            if (_selections != null) _editor.TextArea.TextView.BackgroundRenderers.Remove(_selections);
            if (_carets != null) _editor.TextArea.TextView.BackgroundRenderers.Remove(_carets);
        }
        if (doc == null)
        {
            _editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument();
            _editor.IsReadOnly = true;
            return;
        }
        _editor.Document = doc.Document;
        _editor.SyntaxHighlighting = CodeStyles.ForFile(doc.Path);
        _editor.IsReadOnly = doc.IsReadOnly || !doc.IsLoaded; // typing waits for the server's copy
        _selections = new RemoteSelectionRenderer(doc);
        _carets = new RemoteCaretRenderer(doc);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_selections);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_carets);
        doc.CursorsChanged += Redraw;
        doc.PropertyChanged += OnDocumentPropertyChanged;
        ApplySettings();
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveDocument.Path) && Document != null)
            _editor.SyntaxHighlighting = CodeStyles.ForFile(Document.Path);
        if (e.PropertyName == nameof(LiveDocument.IsLoaded) && Document != null)
            _editor.IsReadOnly = Document.IsReadOnly || !Document.IsLoaded;
    }

    private void Redraw()
    {
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        _flagTimer.Stop();
        _flagTimer.Start();
    }

    private void ReportSelection()
    {
        if (Document == null || !_editor.TextArea.IsKeyboardFocusWithin) return;
        var selection = _editor.TextArea.Selection;
        int head = _editor.CaretOffset;
        int anchor = selection.IsEmpty ? head : (selection.SurroundingSegment.Offset == head ? selection.SurroundingSegment.EndOffset : selection.SurroundingSegment.Offset);
        Document.SetLocalSelection(anchor, head);
    }

    public void FocusEditor() => _editor.TextArea.Focus();
}
