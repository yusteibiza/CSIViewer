using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CSIViewer.Presentation;

/// <summary>Flows command groups onto another line instead of clipping translated labels.</summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(8d, (owner, _) => ((WrapPanel)owner).InvalidateMeasure()));
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width) { width = Math.Max(width, x - Spacing); x = 0; y += rowHeight + Spacing; rowHeight = 0; }
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(Math.Max(width, Math.Max(0, x - Spacing)), y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width) { x = 0; y += rowHeight + Spacing; rowHeight = 0; }
            child.Arrange(new Rect(x, y, Math.Min(size.Width, finalSize.Width), size.Height));
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return finalSize;
    }
}
