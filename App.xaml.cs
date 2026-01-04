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

    private Mutex? _instanceMutex;
    private const string MutexName = "Global\\RadiMenuInstanceMutex";

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Single Instance Check
        bool createdNew;
        _instanceMutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // App is already running. Check for arguments.
            if (e.Args.Length > 0)
            {
                // Send args to main instance
                // For now, join them. Protocol: "ADD:<path>" or just raw args
                // Let's assume "--add-item <path>" structure
                string message = string.Join(" ", e.Args);
                PipeManager.SendMessage(message);
            }
            
            // Exit this instance
            Shutdown();
            return;
        }
        
        base.OnStartup(e);
        
        // Start IPC Server
        var pipeManager = new PipeManager();
        pipeManager.StartServer(OnIpcMessageReceived);
        
        // Initialize Configuration
        Config = new ConfigurationService();

        // 2. Original Startup Logic
        // Initialize Tray Icon using Custom Generated Icon
        var appIcon = Services.IconGenerator.GenerateAppIcon();
        
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = appIcon,
            Visible = true,
            Text = "RadiMenu"
        };

        // Context Menu for Tray Icon
        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Configuración", null, (s, args) => OpenSettings());
        contextMenu.Items.Add("Salir", null, (s, args) => Shutdown());
        _notifyIcon.ContextMenuStrip = contextMenu;
        
        // Handle clicks on tray icon
        _notifyIcon.Click += (s, args) => 
        {
             // Optional: Toggle menu or show settings on left click?
        };

        // Create main window (hidden by default)
        _mainWindow = new MainWindow();
        
        // Setup global hotkey
        _hotkeyService = new Services.HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        
        // Register default hotkey or from config
        _hotkeyService.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);

        // Start Process Monitor
        Monitor = new ProcessMonitorService();
        Monitor.Start();
        
        // Auto-generate app.ico for Context Menu
        string exeDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName) ?? "";
        Services.IconGenerator.EnsureIconFile(System.IO.Path.Combine(exeDir, "app.ico"));

        // Auto-register context menu (User request)
        Services.RegistryManager.RegisterContextMenu();
        
        // Check if THIS new instance was started with args (e.g. first run via context menu)
        if (e.Args.Length > 0)
        {
             HandleCommand(string.Join(" ", e.Args));
        }
    }

    private void OnIpcMessageReceived(string message)
    {
        // Marshal to UI thread
        Dispatcher.Invoke(() => HandleCommand(message));
    }
    
    private void HandleCommand(string command)
    {
        // Parse command: e.g. "--add-item "C:\Foo\Bar.exe""
        if (string.IsNullOrWhiteSpace(command)) return;
        
        // Simple manual parsing needed because args come joined
        // Or we could enforce a prefix
        
        if (command.Contains("--add-item"))
        {
            // Extract path
            // Remove the flag
            string pathRaw = command.Replace("--add-item", "").Trim();
            // Remove quotes if present
            string path = pathRaw.Trim('"');
            
            if (!string.IsNullOrEmpty(path))
            {
                // Show a quick visual confirmation (e.g. from Tray or MessageBox for now)
                _notifyIcon?.ShowBalloonTip(3000, "RadiMenu", $"Agregando: {System.IO.Path.GetFileName(path)}", ToolTipIcon.Info);
                
                // TODO: Actually add the item to configuration
                AddItemFromPath(path);
            }
        }
    }
    
    private void AddItemFromPath(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path)) return;

            // 1. Extract Info
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            // Limit name length
            if (name.Length > 12) name = name.Substring(0, 10) + "..";
            
            // For now, we will use a generic icon or try to use "Application" 
            // Real icon extraction from EXE requires Icon.ExtractAssociatedIcon + converting to Geometry or saving image
            // Simplification: Use "Application" icon from our resource dictionary or a generic path.
            // BETTER: Use the path itself as the "Icon" property and update RadialMenuItem to handle file paths?
            // Existing logic uses resource keys (Geometry). 
            // Let's assume we use a default icon "App" for now.
            string iconKey = "Application"; 

            // 2. Create Model
            var newItem = new RadiMenu.Models.MenuItem
            {
                Label = name,
                Icon = iconKey,
                AppPath = path // Use explicit AppPath property instead of ActionType/Value
            };

            // 3. Add to Config
            if (Config?.ActiveProfile != null)
            {
                if (Config.ActiveProfile.MenuItems == null) 
                    Config.ActiveProfile.MenuItems = new List<RadiMenu.Models.MenuItem>();
                
                // Add to list
                Config.ActiveProfile.MenuItems.Add(newItem);
                
                // Save
                Config.SaveCurrentProfile();
                
                // 4. Reload Menu
                Dispatcher.Invoke(() => 
                {
                   _mainWindow?.ReloadMenu();
                   _notifyIcon?.ShowBalloonTip(2000, "RadiMenu", $"Agregado: {name}", ToolTipIcon.Info);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding item: {ex.Message}");
        }
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
