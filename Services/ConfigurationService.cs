using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadiMenu.Models;

namespace RadiMenu.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "settings.json";
    private string _configPath;
    
    public AppSettings CurrentSettings { get; private set; }

    public ProfileModel ActiveProfile 
    {
        get 
        {
            var profile = CurrentSettings.Profiles.FirstOrDefault(p => p.Id == CurrentSettings.ActiveProfileId);
            if (profile == null)
            {
                // Fallback if ID is wrong
                profile = CurrentSettings.Profiles.FirstOrDefault();
                if (profile == null)
                {
                    // Emergency recovery
                    profile = CreateDefaultProfile();
                    CurrentSettings.Profiles.Add(profile);
                    CurrentSettings.ActiveProfileId = profile.Id;
                }
            }
            return profile;
        }
    }

    public ConfigurationService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "RadiMenu");
        Directory.CreateDirectory(appFolder);
        _configPath = Path.Combine(appFolder, ConfigFileName);
        
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                string json = File.ReadAllText(_configPath);
                // Attempt deserialize
                CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json);
                
                // Validate if null or empty (migration check essentially)
                if (CurrentSettings == null || CurrentSettings.Profiles == null || !CurrentSettings.Profiles.Any())
                {
                    // Likely old format or broken file -> Reset
                    CurrentSettings = CreateDefaultSettings();
                    SaveSettings();
                }
            }
            catch
            {
                CurrentSettings = CreateDefaultSettings();
                SaveSettings();
            }
        }
        else
        {
            CurrentSettings = CreateDefaultSettings();
            SaveSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(CurrentSettings, options);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    public void SaveCurrentProfile()
    {
        // Since ActiveProfile is a reference to an object inside CurrentSettings,
        // we just need to save the whole settings object.
        SaveSettings();
    }

    private AppSettings CreateDefaultSettings()
    {
        var defaultProfile = CreateDefaultProfile();
        
        return new AppSettings
        {
            StartWithWindows = false,
            AnimationSpeed = 1.0,
            GlobalHotkey = "Ctrl+Alt+Space",
            ActiveProfileId = defaultProfile.Id,
            Profiles = new List<ProfileModel> { defaultProfile }
        };
    }
    
    public ProfileModel CreateDefaultProfile()
    {
        return new ProfileModel
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            MenuItems = new List<MenuItem>
            {
                new() { Label = "Volume", Icon = "Speaker2", Shortcut = "VolumeUp" },
                new() { Label = "Copy", Icon = "Copy", Shortcut = "Ctrl+C" },
                new() { Label = "Paste", Icon = "ClipboardPaste", Shortcut = "Ctrl+V" },
                new() { Label = "TaskMgr", Icon = "ReadingList", Command = "taskmgr.exe" },
                new() { Label = "Browser", Icon = "Earth", AppPath = "explorer.exe" }
            },
            Appearance = new AppearanceSettings
            {
                BackgroundColor = "#FF1E1E1E",
                AccentColor = "#FF0078D4",
                IndicatorThickness = 4.0,
                RingThickness = 85.0,
                CenterRadius = 45.0
            }
        };
    }
}
