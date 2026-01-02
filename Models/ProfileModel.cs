using System;
using System.Collections.Generic;
using System.Windows; // For Point

namespace RadiMenu.Models;

public enum PositionMode
{
    FollowMouse,
    Fixed
}

public class ProfileModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public string? TriggerAppPath { get; set; } // Process name or path, e.g. "photoshop"
    
    public List<MenuItem> MenuItems { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    
    public PositionMode PositionMode { get; set; } = PositionMode.FollowMouse;
    public System.Windows.Point FixedPosition { get; set; }
}
