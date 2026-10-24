using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GitHubDemo.Core;

// XAML value converters (declared once in Themes/Design.xaml).
public static class Converters
{
    public static Brush ColorNameToBrush(string? name)
    {
        try
        {
            if (!string.IsNullOrEmpty(name) && ColorConverter.ConvertFromString(name) is Color color)
                return ThemeManager.Solid(color);
        }
        catch (FormatException) { }
        return Brushes.Transparent;
    }
}

// true -> Visible. Parameter "invert" flips it.
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != (parameter as string == "invert");
}

public sealed class InverseBool : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);
}

// null / empty string / 0 -> Collapsed. Parameter "invert" flips it.
public sealed class HasValueToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value switch
        {
            null => false,
            string s => s.Length > 0,
            int i => i != 0,
            long l => l != 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// value == parameter (as text). Used for tabs and sections: IsChecked="{Binding Section, Converter=..., ConverterParameter=editor}".
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool equal = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (targetType == typeof(Visibility)) return equal ? Visibility.Visible : Visibility.Collapsed;
        return equal;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? "" : Binding.DoNothing;
}

// Resource key ("Theme.Online") -> the brush.
public sealed class ResourceKeyToBrush : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) as Brush : null;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ColorNameToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Converters.ColorNameToBrush(value as string);
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// A Color -> SolidColorBrush (remote cursor colours).
public sealed class ColorToBrush : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color c ? ThemeManager.Solid(c) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Number -> "99+" style badge text.
public sealed class BadgeText : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long l ? (l > 99 ? "99+" : l.ToString(culture)) : value is int i ? (i > 99 ? "99+" : i.ToString(culture)) : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Role key -> display label.
public sealed class RoleLabel : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "owner" => "Owner",
        "admin" => "Admin",
        "member" => "Member",
        _ => "Viewer",
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Two values equal (as text) -> true. Used to check the selected filter chip.
public sealed class EqualsMulti : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
