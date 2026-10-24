using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GitHubDemo.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace GitHubDemo.Controls;

// Draws other people's selections (under the text) in their colour.
public sealed class RemoteSelectionRenderer : IBackgroundRenderer
{
    private readonly LiveDocument _doc;

    public RemoteSelectionRenderer(LiveDocument doc) => _doc = doc;

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        int length = textView.Document?.TextLength ?? 0;
        foreach (var cursor in _doc.Cursors)
        {
            int start = Math.Clamp(Math.Min(cursor.Anchor, cursor.Head), 0, length);
            int end = Math.Clamp(Math.Max(cursor.Anchor, cursor.Head), 0, length);
            if (start == end) continue;
            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = start, EndOffset = end });
            var geometry = builder.CreateGeometry();
            if (geometry == null) continue;
            var c = cursor.Color;
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x48, c.R, c.G, c.B)), null, geometry);
        }
    }
}

// Draws other people's carets with a small name flag (over the text).
public sealed class RemoteCaretRenderer : IBackgroundRenderer
{
    private readonly LiveDocument _doc;

    public RemoteCaretRenderer(LiveDocument doc) => _doc = doc;

    public KnownLayer Layer => KnownLayer.Caret;

    public const double FlagSeconds = 3;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (textView.Document == null) return;
        int length = textView.Document.TextLength;
        double pixels = VisualTreeHelper.GetDpi(textView).PixelsPerDip;
        foreach (var cursor in _doc.Cursors)
        {
            int offset = Math.Clamp(cursor.Head, 0, length);
            var location = textView.Document.GetLocation(offset);
            var position = new TextViewPosition(location);
            Point top = textView.GetVisualPosition(position, VisualYPosition.LineTop) - textView.ScrollOffset;
            Point bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom) - textView.ScrollOffset;
            if (double.IsNaN(top.X) || bottom.Y < 0 || top.Y > textView.ActualHeight) continue;

            var brush = new SolidColorBrush(cursor.Color);
            brush.Freeze();
            dc.DrawRectangle(brush, null, new Rect(top.X - 1, top.Y, 2, bottom.Y - top.Y));
            // The name flag would hide the line above: show it only just after they move.
            if ((DateTime.Now - cursor.MovedAt).TotalSeconds > FlagSeconds) continue;

            var label = new FormattedText(cursor.User.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 10.5, Brushes.White, pixels);
            double y = top.Y - label.Height - 1;
            if (y < 0) y = bottom.Y + 1; // first line: put the flag under the caret
            var flag = new Rect(top.X - 1, y, label.Width + 8, label.Height + 1);
            dc.DrawRoundedRectangle(brush, null, flag, 3, 3);
            dc.DrawText(label, new Point(flag.X + 4, flag.Y + 0.5));
        }
    }
}
