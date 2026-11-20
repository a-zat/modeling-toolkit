using Avalonia.Controls;

namespace ModelingToolkit.Gui.Views;

/// <summary>A grid column that can be hidden, and shown again with the width the user gave it with the splitter.</summary>
internal sealed class CollapsibleColumn(ColumnDefinition column)
{
    private GridLength _width = column.Width;

    public void Show(bool visible)
    {
        if (!visible && column.Width.Value > 0)
        {
            _width = column.Width;
        }

        column.Width = visible ? _width : new GridLength(0);
    }
}

/// <summary>
/// A row of a grid that can be hidden and shown again at the height it had, with the row it shares the space with: a
/// splitter between them sets both, as stars in proportion to their heights in points, which neither keeps by itself.
/// A row taken down to nothing by the splitter is shown at a minimum, so that it can be seen to be opened again.
/// </summary>
internal sealed class CollapsibleRow(RowDefinition row, RowDefinition other)
{
    private GridLength _height = row.Height;
    private GridLength _otherHeight = other.Height;
    private bool _hidden;

    /// <summary>The share of the space that a row shown takes at least, in proportion to the other.</summary>
    private const double MinimumShare = 0.15;

    public void Show(bool visible)
    {
        if (!visible)
        {
            if (!_hidden)
            {
                // What the splitter left, to give back.
                _height = row.Height;
                _otherHeight = other.Height;
                _hidden = true;
            }
            row.Height = new GridLength(0);
            return;
        }
        if (_hidden)
        {
            row.Height = _height;
            other.Height = _otherHeight;
            _hidden = false;
        }
        // Both stars, in proportion: a row that the splitter took down to nothing is given a share of the space again.
        if (row.Height.IsStar && other.Height.IsStar)
        {
            double total = row.Height.Value + other.Height.Value;
            if (total > 0 && row.Height.Value < MinimumShare * total)
            {
                row.Height = new GridLength(MinimumShare * total, GridUnitType.Star);
                other.Height = new GridLength((1 - MinimumShare) * total, GridUnitType.Star);
            }
        }
    }
}
