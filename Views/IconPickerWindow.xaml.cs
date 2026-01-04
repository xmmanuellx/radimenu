using System.Windows;
using System.Windows.Controls;
using FluentIcons.Common;
using FluentIcons.Wpf;

namespace RadiMenu.Views;

public partial class IconPickerWindow : Window
{
    private List<IconItem> _allIcons = new();
    private Border? _selectedBorder;
    
    // Legacy: System Icon
    public Symbol? SelectedSymbol { get; private set; }
    
    // New: Universal Icon Name (e.g. "Home" or "mdi:account")
    public string? SelectedIconName { get; private set; }

    public IconPickerWindow()
    {
        InitializeComponent();
        LoadIcons();
        
        // Subscribe to Online View events
        OnlineView.IconSelected += OnOnlineIconSelected;
    }

    private void LoadIcons()
    {
        // Get all icons from the Symbol enum
        _allIcons = Enum.GetValues<Symbol>()
            .Select(s => new IconItem { Symbol = s, Name = s.ToString() })
            .OrderBy(x => x.Name)
            .ToList();
        
        RenderIcons(_allIcons);
    }

    private void RenderIcons(List<IconItem> icons)
    {
        IconsPanel.Children.Clear();
        
        foreach (var item in icons)
        {
            var symbolIcon = new SymbolIcon
            {
                Symbol = item.Symbol,
                FontSize = 22,
                Foreground = System.Windows.Media.Brushes.White
            };
            
            var border = new Border
            {
                Width = 50,
                Height = 50,
                Margin = new Thickness(3),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 62, 66)),
                CornerRadius = new CornerRadius(4),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = item.Name,
                Tag = item,
                Child = symbolIcon
            };
            
            border.MouseDown += Border_MouseDown;
            IconsPanel.Children.Add(border);
        }
    }

    private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is IconItem item)
        {
            // Deselect previous
            if (_selectedBorder != null)
            {
                _selectedBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 62, 66));
            }
            
            // Select new
            _selectedBorder = border;
            border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
            
            SelectedSymbol = item.Symbol;
            SelectedIconName = item.Name; // Fluent icons don't need prefix
            SelectedLabel.Text = $"Seleccionado: {item.Name}";
            
            // Double-click to select
            if (e.ClickCount == 2)
            {
                DialogResult = true;
                Close();
            }
        }
    }

    private void OnOnlineIconSelected(string iconName)
    {
        // Handle selection from the Online View
        SelectedSymbol = null; // Clear system symbol
        SelectedIconName = iconName; // e.g., "mdi:account"
        SelectedLabel.Text = $"Seleccionado: {iconName}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var search = SearchBox.Text.ToLowerInvariant();
        
        if (string.IsNullOrWhiteSpace(search))
        {
            RenderIcons(_allIcons);
        }
        else
        {
            var filtered = _allIcons.Where(x => x.Name.ToLowerInvariant().Contains(search)).ToList();
            RenderIcons(filtered);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedIconName))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            System.Windows.MessageBox.Show("Por favor selecciona un icono primero.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public class IconItem
    {
        public Symbol Symbol { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
