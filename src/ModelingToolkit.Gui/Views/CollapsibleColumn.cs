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
