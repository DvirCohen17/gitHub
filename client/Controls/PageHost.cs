using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GitHubDemo.Controls;

// Shows the current page and animates page changes: the new page fades in and
// slides up slightly, so moving around feels like one app, not windows popping.
public sealed class PageHost : ContentControl
{
    private readonly TranslateTransform _offset = new();

    public PageHost()
    {
        RenderTransform = _offset;
        Focusable = false;
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent == null || SystemParameters.ClientAreaAnimation == false) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        _offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
    }
}
