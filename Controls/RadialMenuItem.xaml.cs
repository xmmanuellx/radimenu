using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using RadiMenu.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace RadiMenu.Controls;

public partial class RadialMenuItem : System.Windows.Controls.UserControl
{
    private bool _isSelected;
    private static readonly IconifyService _iconService = new IconifyService();
    
    public RadialMenuItem()
    {
        InitializeComponent();
        this.DataContextChanged += (s, e) => UpdateIconState();
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        var item = DataContext as RadiMenu.Models.MenuItem;
        if (item == null) return;

        double opacity = _isSelected ? 1.0 : (item.IsToggled ? 1.0 : 0.6);
        FluentIconControl.Opacity = opacity;
        UniversalIconPath.Opacity = opacity;

        if (item.IsToggled && !_isSelected)
        {
            ToggledBackground.Opacity = 0.4;
        }
        else
        {
            ToggledBackground.Opacity = 0;
        }
    }

    private async void UpdateIconState()
    {
        var item = DataContext as RadiMenu.Models.MenuItem;
        if (item == null) return;

        UpdateVisualState();

        // Check if it's a Universal Icon (contains ':')
        if (!string.IsNullOrEmpty(item.Icon) && item.Icon.Contains(":"))
        {
            // Switch to Universal Mode
            FluentIconControl.Visibility = Visibility.Collapsed;
            UniversalIconPath.Visibility = Visibility.Visible;
            
            // Async Fetch
            try
            {
                var data = await _iconService.GetIconDataAsync(item.Icon);
                if (data != null)
                {
                    string pathData = _iconService.ExtractPathData(data.Body);
                    if (!string.IsNullOrEmpty(pathData))
                    {
                        UniversalIconPath.Data = Geometry.Parse(pathData);
                    }
                }
            }
            catch
            {
                // Fallback or empty
                UniversalIconPath.Data = null;
            }
        }
        else
        {
            // Standard Fluent Mode
            FluentIconControl.Visibility = Visibility.Visible;
            UniversalIconPath.Visibility = Visibility.Collapsed;
            
            FluentIcons.Common.Symbol symbol = FluentIcons.Common.Symbol.Question;
            if (!string.IsNullOrEmpty(item.Icon))
            {
                if (System.Enum.TryParse<FluentIcons.Common.Symbol>(item.Icon, out var parsedSymbol))
                {
                    symbol = parsedSymbol;
                }
            }
            FluentIconControl.Symbol = symbol;
        }
    }
}
