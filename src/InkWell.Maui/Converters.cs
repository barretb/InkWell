using System.Globalization;

namespace InkWell.Maui;

/// <summary>
/// Inverts a boolean, so a single "is empty" flag can drive both the empty-state panel and the list
/// without the view model carrying two properties that could disagree.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;
}
