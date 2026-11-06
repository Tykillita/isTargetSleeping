using System.Globalization;
using System.Windows.Data;

namespace IsTargetSleeping.UI;

/// El ancho de la tarjeta de Música en la vista de un widget: el normal, o más ancho (letra al lado) con la vista de letras abierta.
/// Un hijo más ancho que su tarjeta se recortaría, por eso es la tarjeta la que cambia.
public sealed class LyricsWidthConverter : IMultiValueConverter
{
    public static readonly LyricsWidthConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double normal, bool bare, bool open] && bare && open ? 508d : values is [double width, ..] ? width : 356d;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
