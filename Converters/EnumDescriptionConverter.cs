using Avalonia.Data.Converters;
using System;
using System.Globalization;
using BetterNetworkStatus.Models.ComponentSettings;

namespace BetterNetworkStatus.Converters;

public class EnumDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            NetworkDetectMode.Auto => "自动",
            NetworkDetectMode.Icmp => "ICMP 模式",
            NetworkDetectMode.Http => "HTTP 模式",
            SignalDisplayStyle.BarsOnly => "仅信号条",
            SignalDisplayStyle.BarsWithNumber => "信号条 + 数字",
            SignalDisplayStyle.NumberOnly => "仅数字",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
