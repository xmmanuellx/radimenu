using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RadiMenu.Models;

namespace RadiMenu.Services;

public class ProcessMonitorService : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    private WinEventDelegate? _winEventDelegate;
    private IntPtr _hook;
    private bool _isDisposed;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;

        _winEventDelegate = new WinEventDelegate(WinEventProc);
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return;

                var process = Process.GetProcessById((int)pid);
                if (process != null)
                {
                    string processName = process.ProcessName; 
                    // process.MainModule.FileName might require higher privileges or throw for some system processes.
                    // ProcessName is safer first.
                    
                    CheckAndSwitchProfile(processName);
                }
            }
            catch { /* Ignore access denied or exited processes */ }
        }
    }

    private void CheckAndSwitchProfile(string processName)
    {
        // Don't switch if we are in the config window or our own app
        if (processName.Equals("RadiMenu", StringComparison.OrdinalIgnoreCase)) return;

        if (App.Config == null || App.Config.CurrentSettings == null) return;

        var profiles = App.Config.CurrentSettings.Profiles;
        
        // Find profile with matching trigger
        // Assuming TriggerAppPath just holds "notepad" or "photoshop" (case insensitive) for now
        var validProfile = profiles.FirstOrDefault(p => 
            !string.IsNullOrEmpty(p.TriggerAppPath) && 
            p.TriggerAppPath.Contains(processName, StringComparison.OrdinalIgnoreCase));

        if (validProfile != null)
        {
            if (App.Config.ActiveProfile.Id != validProfile.Id)
            {
                Debug.WriteLine($"[AutoSwitch] Switching to profile: {validProfile.Name}");
                SwitchProfile(validProfile.Id);
            }
        }
        else
        {
            // Switch back to Default if current profile has a trigger (meaning we left the triggered app)
            // But if current profile has NO trigger (Manual profile), should we switch back?
            // User requested: "Dynamic profiles linked to programs"
            // Usually, if I switch away from Photoshop to Desktop, I expect "Default".
            // If I was in "Gaming" (Manual) and Alt-Tab to Chrome (No Profile), do I stay "Gaming"?
            // Let's assume: If active profile IS a triggered profile, and we actived an app with NO profile, revert to Default.
            
            var current = App.Config.ActiveProfile;
            if (!string.IsNullOrEmpty(current.TriggerAppPath))
            {
                // Current is dynamic. We moved to neutral ground.
                // Find default profile (assuming Name "Default" or first one with no trigger)
                var defaultProfile = profiles.FirstOrDefault(p => p.Name == "Default") ?? profiles.FirstOrDefault();
                
                if (defaultProfile != null && defaultProfile.Id != current.Id)
                {
                    Debug.WriteLine($"[AutoSwitch] Reverting to default: {defaultProfile.Name}");
                    SwitchProfile(defaultProfile.Id);
                }
            }
        }
    }

    private void SwitchProfile(Guid profileId)
    {
        App.Config.CurrentSettings.ActiveProfileId = profileId;
        App.Config.SaveSettings();

        // Notify UI to update
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ReloadSettings();
            }
        });
    }

    public void Dispose()
    {
        Stop();
    }
}
