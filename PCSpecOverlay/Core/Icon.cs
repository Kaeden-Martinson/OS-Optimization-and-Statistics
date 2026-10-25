using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCSpecOverlay.Core;

/// <summary>Draws one outline icon from Themes/Icons.xaml in the current text color (style in Theme.xaml).</summary>
public sealed class Icon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
