using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MiNest;

/// <summary>品质 -> 颜色（5★金 / 4★紫 / 3★蓝）</summary>
public class RankToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "5" => new SolidColorBrush(Color.FromRgb(0xE0, 0xA9, 0x4B)),
            "4" => new SolidColorBrush(Color.FromRgb(0xB4, 0x77, 0xF0)),
            "3" => new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)),
            _ => new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA3)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>品质 -> 星级文字（5→5★）</summary>
public class RankToStarTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => $"{value}★";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
