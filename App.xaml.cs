using System.Windows;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using RadiMenu.Services;
using RadiMenu.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox; // Default to WPF MessageBox
using Forms = System.Windows.Forms;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace RadiMenu;

public partial class App : Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private Services.HotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    
    // Global Configuration Access
    public static ConfigurationService Config { get; private set; } = null!;
    public static ProcessMonitorService Monitor { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Initialize Configuration
        Config = new ConfigurationService();

        // 1. Initialize Tray Icon
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application, 
            Visible = true,
            Text = "RadiMenu"
        };

        // 2. Context Menu for Tray Icon
        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Configuración", null, (s, args) => OpenSettings());
        contextMenu.Items.Add("Salir", null, (s, args) => Shutdown());
        _notifyIcon.ContextMenuStrip = contextMenu;
        
        // 3. Handle clicks on tray icon
        _notifyIcon.Click += (s, args) => 
        {
             // Optional: Toggle menu or show settings on left click?
             // For now, let's just make sure it doesn't crash.
        };

        // Create main window (hidden by default)
        _mainWindow = new MainWindow();
        
        // Setup global hotkey
        _hotkeyService = new Services.HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        
        // Register default hotkey or from config
        // Parsing "Ctrl+Alt+Space" is needed. For now hardcode or parse simple.
        _hotkeyService.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);

        // Start Process Monitor
        Monitor = new ProcessMonitorService();
        Monitor.Start();
    }
    
    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            if (_mainWindow.Visibility == Visibility.Visible)
            {
                _mainWindow.HideMenu();
            }
            else
            {
                _mainWindow.ShowMenu();
            }
        }
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _hotkeyService?.Dispose();
        Monitor?.Dispose();
        base.OnExit(e);
    }
}
