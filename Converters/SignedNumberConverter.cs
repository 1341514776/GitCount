using System.Globalization;
using System.Windows.Data;

namespace GitCount.Converters;

public sealed class SignedNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var number = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => 0L
        };

        var formatted = Math.Abs(number).ToString("N0", culture);
        return number > 0 ? "+" + formatted : number < 0 ? "-" + formatted : "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
