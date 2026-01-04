using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace RadiMenu.Services;

public static class RegistryManager
{
    private const string MenuName = "Add to RadiMenu";
    private const string CommandFlag = "--add-item";

    public static void RegisterContextMenu()
    {
        try
        {
            // Get current executable path
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath)) return;

            // 1. Register for generic files (*)
            RegisterForKey("*", exePath);
            
            // 2. Register for directories (Directory)
            RegisterForKey("Directory", exePath);

            System.Diagnostics.Debug.WriteLine("[Registry] Context menu registered.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Registry] Error registering: {ex.Message}");
        }
    }

    public static void UnregisterContextMenu()
    {
        try
        {
            UnregisterForKey("*");
            UnregisterForKey("Directory");
            System.Diagnostics.Debug.WriteLine("[Registry] Context menu unregistered.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Registry] Error unregistering: {ex.Message}");
        }
    }

    private static void RegisterForKey(string fileType, string exePath)
    {
        // HKCU\Software\Classes\<fileType>\shell\RadiMenu
        string keyPath = $@"Software\Classes\{fileType}\shell\RadiMenu";

        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (key != null)
        {
            key.SetValue("", MenuName); // Display Name
            string iconPath = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "app.ico");
            if (File.Exists(iconPath))
            {
                key.SetValue("Icon", $"\"{iconPath}\"");
            }
            else
            {
                key.SetValue("Icon", $"\"{exePath}\",0"); // Use EXE icon
            }

            using var commandKey = key.CreateSubKey("command");
            if (commandKey != null)
            {
                // "C:\Path\RadiMenu.exe" --add-item "%1"
                string command = $"\"{exePath}\" {CommandFlag} \"%1\"";
                commandKey.SetValue("", command);
            }
        }
    }

    private static void UnregisterForKey(string fileType)
    {
        string keyPath = $@"Software\Classes\{fileType}\shell\RadiMenu";
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
    }
}
