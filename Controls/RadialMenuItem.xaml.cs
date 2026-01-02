using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace RadiMenu.Controls;

public partial class RadialMenuItem : System.Windows.Controls.UserControl
{
    private bool _isSelected;
    
    public RadialMenuItem()
    {
        InitializeComponent();
        this.DataContextChanged += (s, e) => UpdateIconState();
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateIconState();
    }

    private void UpdateIconState()
    {
        var item = DataContext as RadiMenu.Models.MenuItem;
        if (item == null) return;

        // Convert string icon name to Symbol enum
        FluentIcons.Common.Symbol symbol = FluentIcons.Common.Symbol.Question; // Default fallback
        
        if (!string.IsNullOrEmpty(item.Icon))
        {
            if (System.Enum.TryParse<FluentIcons.Common.Symbol>(item.Icon, out var parsedSymbol))
            {
                symbol = parsedSymbol;
            }
        }
        
        FluentIconControl.Symbol = symbol;

        // Apply opacity and background based on selection/toggle state
        double opacity = _isSelected ? 1.0 : (item.IsToggled ? 1.0 : 0.6);
        FluentIconControl.Opacity = opacity;

        if (item.IsToggled && !_isSelected)
        {
            ToggledBackground.Opacity = 0.4;
        }
        else
        {
            ToggledBackground.Opacity = 0;
        }
    }
}
