using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PurviewCopilot.Domain;

namespace PurviewCopilot.UI.Converters;

public sealed class ScoreToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var score = value is double d ? d : 0;
        var color = score >= 70 ? "#2ECC71" : score >= 45 ? "#F5B041" : "#E74C3C";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var severity = value is Severity s ? s : Severity.Info;
        var color = severity switch
        {
            Severity.Critical => "#E74C3C",
            Severity.High => "#F0932B",
            Severity.Medium => "#F5B041",
            Severity.Low => "#4CC2FF",
            _ => "#93A6C4"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value is FindingStatus s ? s : FindingStatus.NotAssessed;
        var color = status switch
        {
            FindingStatus.Pass => "#2ECC71",
            FindingStatus.Warning => "#F5B041",
            FindingStatus.Fail => "#E74C3C",
            _ => "#93A6C4"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
