using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Sentinel.CLI.Tui.Views;

// One rendered trace row: the full text, the service color for the bulk of the line, and the
// status color applied to just the status token at [StatusStart, StatusStart+StatusLength).
internal readonly record struct TraceRow(
    string Text, Color ServiceColor, Color StatusColor, int StatusStart, int StatusLength);

// A ListView data source that colors each row by service but paints only the status token in
// the status color. ListView's RowRender can set one attribute per row, not per column, so the
// per-segment coloring requires drawing the row here. Count/marking/events are delegated to an
// inner ListWrapper; only Render is custom — a per-column draw, which is correct because rows
// are ASCII apart from the em-dash unset marker (a wide/surrogate service.name would mis-align).
internal sealed class TraceListSource : IListDataSource
{
    private readonly IReadOnlyList<TraceRow> _rows;
    private readonly ListWrapper<string> _inner;

    public TraceListSource(IReadOnlyList<TraceRow> rows)
    {
        _rows = rows;
        _inner = new ListWrapper<string>(new ObservableCollection<string>(rows.Select(r => r.Text)));
    }

    public int Count => _inner.Count;
    public int MaxItemLength => _inner.MaxItemLength;

    public bool SuspendCollectionChangedEvent
    {
        get => _inner.SuspendCollectionChangedEvent;
        set => _inner.SuspendCollectionChangedEvent = value;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => _inner.CollectionChanged += value;
        remove => _inner.CollectionChanged -= value;
    }

    public bool IsMarked(int item) => _inner.IsMarked(item);
    public void SetMark(int item, bool value) => _inner.SetMark(item, value);
    public IList ToList() => _inner.ToList();
    public bool RenderMark(ListView container, int item, int col, bool selected, bool marked)
        => _inner.RenderMark(container, item, col, selected, marked);

    public void Render(
        ListView container, bool selected, int item, int col, int row, int width, int viewportX)
    {
        ArgumentNullException.ThrowIfNull(container);
        // The selected row keeps the normal focus highlight while the list is focused.
        var useFocus = selected && container.HasFocus;
        var focus = container.GetAttributeForRole(VisualRole.Focus);
        var background = container.GetAttributeForRole(VisualRole.Normal).Background;
        var r = item >= 0 && item < _rows.Count ? _rows[item] : default;
        var text = r.Text ?? string.Empty;

        for (var c = 0; c < width; c++)
        {
            var index = c + viewportX; // horizontal scroll offset
            var ch = index >= 0 && index < text.Length ? text[index] : ' ';
            TgAttribute attribute;
            if (useFocus)
            {
                attribute = focus;
            }
            else
            {
                var inStatus = index >= r.StatusStart && index < r.StatusStart + r.StatusLength;
                attribute = new TgAttribute(inStatus ? r.StatusColor : r.ServiceColor, background);
            }
            // Mirror the built-in renderer's Move-then-AddRune pattern so the coordinate space
            // matches what ListView passes in.
            container.SetAttribute(attribute);
            container.Move(col + c, row);
            container.AddRune(new Rune(ch));
        }
    }

    public void Dispose() => _inner.Dispose();
}
