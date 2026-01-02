namespace RadiMenu.Models;

public class MenuItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty; // Fluent UI icon name
    
    // Actions - serialized properties
    public string? Shortcut { get; set; } // Keyboard shortcut to execute
    public string? Command { get; set; } // System command to run
    public string? AppPath { get; set; } // Application to launch
    
    // UI State for Quick Config
    public bool IsToggled { get; set; } = false;
    public bool KeepOpen { get; set; } = false;
    
    // Runtime properties (excluded from JSON if using default serializer, need [JsonIgnore] if necessary)
    // System.Text.Json by default ignores fields/properties it can't map or private?
    // Actually need to mark Action as ignored if it causes issues, but Action is a Delegate, which is NOT serializable.
    // We should attribute it.
    
    [System.Text.Json.Serialization.JsonIgnore]
    public Action? Action { get; set; }
    
    // Submenu support
    public List<MenuItem>? SubItems { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSubItems => SubItems != null && SubItems.Count > 0;
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSubmenuOnly => HasSubItems 
        && string.IsNullOrEmpty(Shortcut) 
        && string.IsNullOrEmpty(Command) 
        && string.IsNullOrEmpty(AppPath) 
        && Action == null;

    
    // UI binding properties (should be ignored for cleaner config file, but technically harmless if saved)
    [System.Text.Json.Serialization.JsonIgnore]
    public double Angle { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSelected { get; set; }
}
