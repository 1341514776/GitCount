using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GitCount.Converters;

public sealed class NetChangeBrushConverter : IValueConverter
{
    private static readonly Brush PositiveBrush = CreateBrush("#12B76A");
    private static readonly Brush NegativeBrush = CreateBrush("#F04438");
    private static readonly Brush NeutralBrush = CreateBrush("#667085");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var net = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => 0L
        };

        return net > 0 ? PositiveBrush : net < 0 ? NegativeBrush : NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
