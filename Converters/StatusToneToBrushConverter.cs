using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GitCount.ViewModels;

namespace GitCount.Converters;

public sealed class StatusToneToBrushConverter : IValueConverter
{
    private static readonly Brush Neutral = CreateBrush("#98A2B3");
    private static readonly Brush Info = CreateBrush("#2E90FA");
    private static readonly Brush Success = CreateBrush("#12B76A");
    private static readonly Brush Warning = CreateBrush("#F79009");
    private static readonly Brush Error = CreateBrush("#F04438");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is StatusTone tone
            ? tone switch
            {
                StatusTone.Info => Info,
                StatusTone.Success => Success,
                StatusTone.Warning => Warning,
                StatusTone.Error => Error,
                _ => Neutral
            }
            : Neutral;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
