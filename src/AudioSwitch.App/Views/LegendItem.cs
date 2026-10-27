using System.Windows;

namespace AudioSwitch.App.Views;

/// <summary>A colour swatch with its meaning, used for the port status legend.</summary>
public sealed class LegendItem : System.Windows.Controls.StackPanel
{
    private readonly System.Windows.Shapes.Ellipse _swatch = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Controls.TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

    public LegendItem()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        _label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _label.FontSize = 12;
        Children.Add(_swatch);
        Children.Add(_label);
    }

    private string _brushKey = PortShapes.NeutralBrushKey;

    public string BrushKey
    {
        get => _brushKey;
        set
        {
            _brushKey = value;
            _swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, value);
        }
    }
    public string Text { get => _label.Text; set => _label.Text = value; }

    /// <summary>Shows a filled badge with this glyph instead of a colour dot, matching the current-device badges on the drawing.</summary>
    public string? Glyph
    {
        get => (_badge?.Child as System.Windows.Controls.TextBlock)?.Text;
        set
        {
            if (_badge is null)
            {
                var icon = new System.Windows.Controls.TextBlock { FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(System.Windows.Controls.TextBlock.FontFamilyProperty, "IconFont");
                icon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
                _badge = new System.Windows.Controls.Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = icon
                };
                _badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentFillColorDefaultBrush");
                Children.Remove(_swatch);
                Children.Insert(0, _badge);
            }
            ((System.Windows.Controls.TextBlock)_badge.Child).Text = value;
        }
    }

    private System.Windows.Controls.Border? _badge;
}
