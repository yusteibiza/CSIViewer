using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSIViewer.Presentation;

/// <summary>Theme-aware hover for action rows that intentionally cannot be selected.</summary>
public static class RowHover
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(object), typeof(RowHover), new PropertyMetadata(null));
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(RowHover), new PropertyMetadata(null, OnBrushChanged));

    public static void SetBrush(DependencyObject owner, Brush value) => owner.SetValue(BrushProperty, value);
    public static Brush GetBrush(DependencyObject owner) => (Brush)owner.GetValue(BrushProperty);

    private static void OnBrushChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        if (owner is not Grid row) return;
        if (row.GetValue(StateProperty) is not HoverState state)
        {
            state = new HoverState(row);
            row.SetValue(StateProperty, state);
        }
        state.Refresh();
    }

    private sealed class HoverState
    {
        private readonly Grid _row;
        private readonly Brush? _normal;
        private bool _hovered;

        public HoverState(Grid row)
        {
            _row = row;
            _normal = row.Background ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            row.PointerEntered += (_, _) => { _hovered = true; Refresh(); };
            row.PointerExited += (_, e) =>
            {
                var p = e.GetCurrentPoint(row).Position;
                _hovered = p.X >= 0 && p.Y >= 0 && p.X < row.ActualWidth && p.Y < row.ActualHeight;
                Refresh();
            };
            row.Unloaded += (_, _) => { _hovered = false; Refresh(); };
        }

        public void Refresh() => _row.Background = _hovered ? GetBrush(_row) : _normal;
    }
}
