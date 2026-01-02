using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MenuItemModel = RadiMenu.Models.MenuItem;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
namespace RadiMenu;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Deactivated += MainWindow_Deactivated;
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
             // If config says we should close on outside click (which causes deactivation)
             if (App.Config.CurrentSettings.CloseOnOutsideClick)
             {
                 HideMenu();
             }
        }
    }

    public void ShowMenu()
    {
        // 1. Determine Position
        var profile = App.Config.ActiveProfile;
        double targetX = 0;
        double targetY = 0;
        
        // Common DPI Calculation
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        if (profile.PositionMode == RadiMenu.Models.PositionMode.Fixed)
        {
            // Fixed Position is stored in Screen Pixels via PointToScreen
            // Must convert to DIPs before applying to Window.Left/Top
            double fixedX_DIP = profile.FixedPosition.X / dpiX;
            double fixedY_DIP = profile.FixedPosition.Y / dpiY;

            targetX = fixedX_DIP - (this.Width / 2); 
            targetY = fixedY_DIP - (this.Height / 2);
        }
        else
        {
            // Follow Mouse (Pixels) -> DIPs
            var mousePos = RadiMenu.Services.InputService.GetMousePosition();
            
            double mouseX_DIP = mousePos.X / dpiX;
            double mouseY_DIP = mousePos.Y / dpiY;
            
            targetX = mouseX_DIP - (this.Width / 2);
            targetY = mouseY_DIP - (this.Height / 2);
        }
        
        this.Left = targetX;
        this.Top = targetY;
        
        Visibility = Visibility.Visible;
        
        // Always start with main menu (not config mode), and reset state
        RadialMenuControl.EnsureMainMenu();
        
        // Reset gesture timer for time-based activation
        RadialMenuControl.ResetGestureTimer();
        
        // Fade in animation
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        BackdropGrid.BeginAnimation(OpacityProperty, fadeIn);
        
        // Scale animation for menu
        RadialMenuControl.AnimateIn();
        
        Activate();
        Focus();
    }

    public void HideMenu()
    {
        // Fade out animation
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) => Visibility = Visibility.Hidden;
        BackdropGrid.BeginAnimation(OpacityProperty, fadeOut);
        
        RadialMenuControl.AnimateOut();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close if clicking outside the menu
        var position = e.GetPosition(RadialMenuControl);
        if (!RadialMenuControl.IsPointInMenu(position))
        {
            if (App.Config.CurrentSettings.CloseOnOutsideClick)
            {
                HideMenu();
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideMenu();
        }
    }

    private async void RadialMenuControl_ItemSelected(object? sender, MenuItemModel item)
    {
        // Close menu immediately so focus returns to the previous app
        // UNLESS the item requests to keep menu open (e.g. Config Toggles)
        if (!item.KeepOpen)
        {
            HideMenuImmediate();
            // Small delay to ensure Windows OS switches focus back
            await Task.Delay(150);
        }

        if (!string.IsNullOrEmpty(item.Shortcut))
        {
             // Use our new proper InputService
             await RadiMenu.Services.InputService.SimulateKeyCombo(item.Shortcut);
        }
        else
        {
             if (item.Action != null)
             {
                 item.Action.Invoke();
             }
             else if (!string.IsNullOrEmpty(item.AppPath))
             {
                 try
                 {
                     System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                     {
                         FileName = item.AppPath,
                         UseShellExecute = true
                     });
                 }
                 catch (Exception ex)
                 {
                     System.Windows.MessageBox.Show($"Error al ejecutar: {ex.Message}", "Error");
                 }
             }
             else if (!string.IsNullOrEmpty(item.Command))
             {
                 try
                 {
                     System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                     {
                         FileName = "cmd.exe",
                         Arguments = $"/c {item.Command}",
                         CreateNoWindow = true,
                         UseShellExecute = false
                     });
                 }
                 catch (Exception ex)
                 {
                     System.Windows.MessageBox.Show($"Error al comando: {ex.Message}", "Error");
                 }
             }
        }
    }

    public void HideMenuImmediate()
    {
        Visibility = Visibility.Hidden;
        RadialMenuControl.AnimateOut(); // Reset state but don't wait for animation
    }
    public void ReloadSettings()
    {
        RadialMenuControl.ReloadItems();
    }
}