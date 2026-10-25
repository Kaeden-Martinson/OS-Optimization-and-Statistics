using System.Windows;
using System.Windows.Controls;

namespace PCSpecOverlay;

/// <summary>
/// Lays its child out in the width the rest of the overlay needs and asks for no width of its own, so a line that
/// comes and goes or changes length (the header's badge, the slowdown chips) wraps or trims instead of widening it.
/// </summary>
public sealed class FollowWidth : Decorator
{
    private double _width = double.NaN; // the width it was last given

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        var width = double.IsNaN(_width) ? constraint.Width : Math.Min(_width, constraint.Width);
        Child.Measure(new Size(width, constraint.Height));
        return new Size(0, Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (!size.Width.Equals(_width))
        {
            // Measured before the width was known: measure again so wrapped lines get their height
            _width = size.Width;
            InvalidateMeasure();
        }
        Child?.Arrange(new Rect(size));
        return size;
    }
}
