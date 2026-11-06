using System.Windows;
using System.Windows.Controls;

namespace IsTargetSleeping.UI;

/// El Home como una cuadrícula invisible de dos columnas y filas de altura fija: cada widget ocupa 1 o 2 columnas y las filas que pide su `WidgetSlot`
/// (1×1, 2×1, 1×2, 2×2…); si su contenido no cabe en ellas, se le dan las filas que necesite para que nada quede cortado. Se colocan en orden, cada uno
/// en el primer hueco libre (de arriba abajo, de izquierda a derecha), y todo mide lo mismo: sin huecos desiguales.
public sealed class GridPanel : Panel
{
    public const double CellWidth = 306, RowHeight = 164;

    /// `Height`: lo que mide de verdad; sus filas se reservan enteras para que nada se pise, pero el panel no deja un hueco vacío debajo del último.
    private List<(int Col, int Row, int Cols, int Rows, double Height)> Place()
    {
        var used = new List<bool[]>();   // used[fila][columna]
        bool Free(int row, int col, int cols, int rows)
        {
            for (int r = row; r < row + rows; r++)
                for (int c = col; c < col + cols; c++)
                    if (r < used.Count && used[r][c]) return false;
            return true;
        }
        var result = new List<(int, int, int, int, double)>();
        foreach (UIElement child in InternalChildren)
        {
            var slot = (child as FrameworkElement)?.DataContext as WidgetSlot ?? (child as ContentPresenter)?.Content as WidgetSlot;
            int cols = Math.Clamp(slot?.Span ?? (child.DesiredSize.Width > CellWidth * 1.5 ? 2 : 1), 1, 2);
            int slotRows = slot?.Rows ?? 1;
            int rows = Math.Max(slotRows, (int)Math.Ceiling((child.DesiredSize.Height - 0.5) / RowHeight));
            // El calendario solo conserva su variante amplia, pero no la segunda fila vacía de su celda 2×2.
            double height = slot is { Id: "clock", WideAlone: true } ? child.DesiredSize.Height
                : child.DesiredSize.Height > slotRows * RowHeight ? child.DesiredSize.Height : rows * RowHeight;
            int row = 0, col = 0;
            for (bool found = false; !found; row++)
                for (int c = 0; c <= 2 - cols && !found; c++)
                    if (Free(row, c, cols, rows)) { col = c; found = true; row--; }
            while (used.Count < row + rows) used.Add(new bool[2]);
            for (int r = row; r < row + rows; r++)
                for (int c = col; c < col + cols; c++) used[r][c] = true;
            result.Add((col, row, cols, rows, height));
        }
        return result;
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cells = Place();
        return new Size(CellWidth * 2, cells.Count == 0 ? 0 : cells.Max(c => c.Row * RowHeight + c.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cells = Place();
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var (col, row, cols, _, height) = cells[i];
            InternalChildren[i].Arrange(new Rect(col * CellWidth, row * RowHeight, cols * CellWidth, height));
        }
        return new Size(CellWidth * 2, cells.Count == 0 ? 0 : cells.Max(c => c.Row * RowHeight + c.Height));
    }
}
