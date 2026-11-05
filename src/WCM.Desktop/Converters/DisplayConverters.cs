using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WCM.Desktop.Converters;

public sealed class BytesConverter : IValueConverter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):N1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):N1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):N1} KB",
        _ => $"{bytes:N0} bytes",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? Format(bytes) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Shows content only when a count is zero, for empty states.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class TextToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Shows internal codes as readable words: UnconfirmedCandidate becomes Unconfirmed candidate.</summary>
public sealed partial class HumanizeConverter : IValueConverter
{
    public static string Format(string text)
    {
        var spaced = Boundary().Replace(text, " ");
        return spaced.Length == 0 ? spaced : spaced[0] + spaced[1..].ToLowerInvariant();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? Format(text) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    [System.Text.RegularExpressions.GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial System.Text.RegularExpressions.Regex Boundary();
}
