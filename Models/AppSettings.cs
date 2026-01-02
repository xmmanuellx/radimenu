using System;
using System.Collections.Generic;

namespace RadiMenu.Models;

public class AppSettings
{
    // Global Settings
    public bool StartWithWindows { get; set; } = false;
    public double AnimationSpeed { get; set; } = 1.0;
    public string GlobalHotkey { get; set; } = "Ctrl+Alt+Space";
    public bool CloseOnOutsideClick { get; set; } = true;
    public bool EnableGestureActivation { get; set; } = false;
    public double GestureTimeLimit { get; set; } = 500.0; // Milliseconds to activate gesture
    
    // Submenu Settings
    public double SubmenuHoverDelay { get; set; } = 300.0; // ms to hover before opening submenu
    public int MaxSubmenuDepth { get; set; } = 3; // Max nesting levels
    
    // Profiles
    public Guid ActiveProfileId { get; set; }
    public List<ProfileModel> Profiles { get; set; } = new();
}
