using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RadiMenu.Models;
using MenuItemModel = RadiMenu.Models.MenuItem;
// Explicit Namespace Aliases to avoid ambiguity
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

using System.Windows.Input;
 
namespace RadiMenu.Views;

public partial class SettingsWindow : Window
{
    private ObservableCollection<MenuItemModel> _menuItems = new();
    private ObservableCollection<ProfileModel> _profiles = new();
    private ProfileModel? _editingProfile; // The profile currently being edited (may not be active)
    
    private MenuItemModel? _selectedItem;
    private bool _isLoading = false;
    private RadiMenu.Services.KeyboardHookService? _hookService;
    private bool _isRecording = false;
    
    // Submenu navigation stack - stores parent items to navigate back
    private Stack<MenuItemModel?> _parentStack = new();
    private List<MenuItemModel>? _currentItemsList;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        
        // Access Config safely
        if (App.Config?.CurrentSettings != null)
        {
            StartWithWindowsCheck.IsChecked = App.Config.CurrentSettings.StartWithWindows;
            CloseOnOutsideCheck.IsChecked = App.Config.CurrentSettings.CloseOnOutsideClick;
            GestureActivationCheck.IsChecked = App.Config.CurrentSettings.EnableGestureActivation;
            GestureTimeLimitSlider.Value = App.Config.CurrentSettings.GestureTimeLimit;
            GlobalHotkeyBox.Text = App.Config.CurrentSettings.GlobalHotkey;
            
            // Profiles
            _profiles = new ObservableCollection<ProfileModel>(App.Config.CurrentSettings.Profiles);
            ProfilesListBox.ItemsSource = _profiles;
            
            // Default editing profile is the active one
            _editingProfile = App.Config.ActiveProfile;
            ProfilesListBox.SelectedItem = _editingProfile;
            
            LoadProfileIntoUI(_editingProfile);
        }
        
        _isLoading = false;
    }

    private void LoadProfileIntoUI(ProfileModel profile)
    {
        // Bind Appearance
        BackgroundColorBox.Text = profile.Appearance.BackgroundColor;
        AccentColorBox.Text = profile.Appearance.AccentColor;
        IndicatorThicknessSlider.Value = profile.Appearance.IndicatorThickness;
        CenterRadiusSlider.Value = profile.Appearance.CenterRadius;
        RingThicknessSlider.Value = profile.Appearance.RingThickness;
            
        // Bind Items
        _menuItems = new ObservableCollection<MenuItemModel>(profile.MenuItems);
        ItemsListBox.ItemsSource = _menuItems;
        
        // Bind Profile Details
        ProfileNameBox.Text = profile.Name;
        ProfileTriggerBox.Text = profile.TriggerAppPath;
        
        // Active Indicator
        if (App.Config?.ActiveProfile?.Id == profile.Id)
            ActiveProfileIndicator.Visibility = Visibility.Visible;
        else
            ActiveProfileIndicator.Visibility = Visibility.Hidden;
    }
    
    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPanel == null || ItemsPanel == null || AboutPanel == null || AppearancePanel == null) return;
        
        // Hide all
        GeneralPanel.Visibility = Visibility.Collapsed;
        ItemsPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Collapsed;
        AppearancePanel.Visibility = Visibility.Collapsed;
        ProfilesPanel.Visibility = Visibility.Collapsed;
        
        int index = NavList.SelectedIndex;
        switch (index)
        {
            case 0: GeneralPanel.Visibility = Visibility.Visible; break;
            case 1: ProfilesPanel.Visibility = Visibility.Visible; break;
            case 2: ItemsPanel.Visibility = Visibility.Visible; break;
            case 3: AppearancePanel.Visibility = Visibility.Visible; break;
            case 4: AboutPanel.Visibility = Visibility.Visible; break;
        }
    }

    private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsListBox.SelectedItem is MenuItemModel item)
        {
            _selectedItem = item;
            ItemDetailsPanel.IsEnabled = true;
            
            // Populate details
            _isLoading = true;
            ItemLabelBox.Text = item.Label;
            ItemIconBox.Text = item.Icon;
            
            // Action Type logic
            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                ActionTypeCombo.SelectedIndex = 1;
                ActionValueBox.Text = item.Shortcut;
            }
            else if (!string.IsNullOrEmpty(item.Command) || !string.IsNullOrEmpty(item.AppPath))
            {
                 ActionTypeCombo.SelectedIndex = 2;
                 ActionValueBox.Text = !string.IsNullOrEmpty(item.Command) ? item.Command : item.AppPath;
            }
            else
            {
                ActionTypeCombo.SelectedIndex = 0;
                ActionValueBox.Text = "";
            }
            UpdateActionValueVisibility();
            
            // Enable submenu edit button
            EditSubItemsBtn.IsEnabled = true;
            
            UpdateIconPreview(); // Update preview when loading item
            _isLoading = false;
        }
        else
        {
            _selectedItem = null;
            ItemDetailsPanel.IsEnabled = false;
            ItemLabelBox.Text = "";
            ItemIconBox.Text = "";
            ActionValueBox.Text = "";
            EditSubItemsBtn.IsEnabled = false;
            UpdateIconPreview(); // Clear preview when no selection
        }
    }

    private void UpdateActionValueVisibility()
    {
        if (ActionTypeCombo.SelectedIndex == 0)
        {
            ActionValuePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ActionValuePanel.Visibility = Visibility.Visible;
            if (ActionTypeCombo.SelectedIndex == 1) ActionValueLabel.Text = "Tecla (ej: Ctrl+C)";
            else ActionValueLabel.Text = "Comando/App";
        }
    }

    private void ItemLabelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem == null) return;
        _selectedItem.Label = ItemLabelBox.Text;
        ItemsListBox.Items.Refresh(); // Refresh list to show new name
    }

    
    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
         if (_isLoading || _selectedItem == null) return;
         UpdateActionValueVisibility();
         UpdateActionValue();
    }
    
    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        ActionValueBox.Text = "";
        ActionValueBox_TextChanged(null, null); // Force update model
    }

    private void ActionValueBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (ActionTypeCombo.SelectedIndex == 1) // Shortcut mode
        {
            StartRecording();
        }
    }

    private void ActionValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private void StartRecording()
    {
        if (_isRecording) return;
        
        _hookService = new RadiMenu.Services.KeyboardHookService();
        _hookService.KeyIntercepted += OnKeyIntercepted;
        _hookService.Start();
        _isRecording = true;
        _pressedKeys.Clear();
        
        ActionValueBox.Text = "Presiona teclas...";
        ActionValueBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
    }

    private void StopRecording()
    {
        if (!_isRecording) return;
        
        if (_hookService != null)
        {
            _hookService.Stop();
            _hookService.KeyIntercepted -= OnKeyIntercepted;
            _hookService.Dispose();
            _hookService = null;
        }
        _isRecording = false;
        _pressedKeys.Clear();
        ActionValueBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        
        // Restore if empty? No, let it be.
    }

    private HashSet<Key> _pressedKeys = new HashSet<Key>();

    private void OnKeyIntercepted(Key key, bool isDown)
    {
         // Update state
         if (isDown) _pressedKeys.Add(key);
         else _pressedKeys.Remove(key);
         
         if (!isDown) return; // Only update UI on KeyDown
         
         // Ignore "System" dummy key IF it is just that generic one, but usually we get specific keys.
         if (key == Key.System) return; 

         Dispatcher.Invoke(() =>
         {
             var modifiers = new List<string>();
             
             // Check modifiers actively from our local state
             if (_pressedKeys.Contains(Key.LeftCtrl) || _pressedKeys.Contains(Key.RightCtrl)) modifiers.Add("Ctrl");
             if (_pressedKeys.Contains(Key.LeftAlt) || _pressedKeys.Contains(Key.RightAlt)) modifiers.Add("Alt");
             if (_pressedKeys.Contains(Key.LeftShift) || _pressedKeys.Contains(Key.RightShift)) modifiers.Add("Shift");
             if (_pressedKeys.Contains(Key.LWin) || _pressedKeys.Contains(Key.RWin)) modifiers.Add("Win");
             
             // If key is Modifier itself, don't double add
             string keyStr = key.ToString();
             
             if (key == Key.LeftCtrl || key == Key.RightCtrl) keyStr = "";
             if (key == Key.LeftAlt || key == Key.RightAlt) keyStr = "";
             if (key == Key.LeftShift || key == Key.RightShift) keyStr = "";
             if (key == Key.LWin || key == Key.RWin) keyStr = "";
             
             // Construct string
             string finalCombo = "";
             if (modifiers.Count > 0) finalCombo = string.Join("+", modifiers);
             if (!string.IsNullOrEmpty(keyStr))
             {
                 if (finalCombo.Length > 0) finalCombo += "+";
                 finalCombo += keyStr;
                 
                 ActionValueBox.Text = finalCombo;
             }
             else
             {
                 // Only modifiers pressed
                  ActionValueBox.Text = finalCombo;
             }
         });
    }

    // Replace PreviewKeyDown with empty handler or remove reference in XAML
    private void ActionValueBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
         // Legacy: handled by Hook now if focused.
         // But if hook fails, this is fallback. 
         // Actually, better to disable this if we use Hook.
         e.Handled = true; 
    }

    private void ActionValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem == null) return;
        UpdateActionValue();
    }
    
    private void UpdateActionValue()
    {
        if (_selectedItem == null) return;
        
        // Clear all first
        _selectedItem.Shortcut = null;
        _selectedItem.Command = null;
        _selectedItem.AppPath = null;
        
        string val = ActionValueBox.Text;
        
        if (ActionTypeCombo.SelectedIndex == 1) // Shortcut
        {
            _selectedItem.Shortcut = val;
        }
        else if (ActionTypeCombo.SelectedIndex == 2) // Command/App
        {
            if (val.EndsWith(".exe") || val.Contains("/") || val.Contains("\\")) _selectedItem.AppPath = val;
            else _selectedItem.Command = val;
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new MenuItemModel 
        { 
            Label = "Nuevo Item", 
            Icon = "Add" // Plus icon
        };
        
        // Add to current list (submenu or main menu)
        if (_currentItemsList != null)
        {
            _currentItemsList.Add(newItem);
            ItemsListBox.ItemsSource = null;
            ItemsListBox.ItemsSource = _currentItemsList;
        }
        else
        {
            _menuItems.Add(newItem);
        }
        
        ItemsListBox.SelectedItem = newItem;
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            // Remove from current list (submenu or main menu)
            if (_currentItemsList != null)
            {
                _currentItemsList.Remove(_selectedItem);
                ItemsListBox.ItemsSource = null;
                ItemsListBox.ItemsSource = _currentItemsList;
            }
            else
            {
                _menuItems.Remove(_selectedItem);
            }
        }
    }

    private void PickBackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.ColorDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            BackgroundColorBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    private void PickAccentColor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.ColorDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            AccentColorBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Config?.CurrentSettings != null)
        {
            App.Config.CurrentSettings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            App.Config.CurrentSettings.CloseOnOutsideClick = CloseOnOutsideCheck.IsChecked == true;
            App.Config.CurrentSettings.EnableGestureActivation = GestureActivationCheck.IsChecked == true;
            App.Config.CurrentSettings.GestureTimeLimit = GestureTimeLimitSlider.Value;

            // Profile Settings
            // Save to the profile currently being edited
            var profileToSave = _editingProfile;
            
            // DEBUG: Log menu items and sub-items
            string debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save_debug.txt");
            System.IO.File.WriteAllText(debugPath, $"Saving {_menuItems.Count} items:\n");
            foreach (var item in _menuItems)
            {
                int subCount = item.SubItems?.Count ?? 0;
                System.IO.File.AppendAllText(debugPath, $"  - {item.Label}: SubItems={subCount}\n");
            }
            if (profileToSave != null)
            {
                profileToSave.MenuItems = _menuItems.ToList();
                
                profileToSave.Appearance.BackgroundColor = BackgroundColorBox.Text;
                profileToSave.Appearance.AccentColor = AccentColorBox.Text;
                profileToSave.Appearance.IndicatorThickness = IndicatorThicknessSlider.Value;
                profileToSave.Appearance.CenterRadius = CenterRadiusSlider.Value;
                profileToSave.Appearance.RingThickness = RingThicknessSlider.Value;
            }
            
            App.Config.SaveSettings();
            
            // Reload Main App
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ReloadSettings();
            }
            
            MessageBox.Show("Configuración guardada.", "RadiMenu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesListBox.SelectedItem is ProfileModel profile)
        {
            _editingProfile = profile;
            ProfileDetailsPanel.IsEnabled = true;
            LoadProfileIntoUI(profile);
            
            // Re-bind items list for editing items
            _menuItems = new ObservableCollection<MenuItemModel>(profile.MenuItems);
            ItemsListBox.ItemsSource = _menuItems;
        }
        else
        {
            ProfileDetailsPanel.IsEnabled = false;
        }
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var newProfile = App.Config.CreateDefaultProfile();
        newProfile.Name = "Nuevo Perfil";
        _profiles.Add(newProfile);
        App.Config.CurrentSettings.Profiles.Add(newProfile);
        
        ProfilesListBox.SelectedItem = newProfile;
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_editingProfile != null && _profiles.Count > 1)
        {
            if (App.Config.ActiveProfile.Id == _editingProfile.Id)
            {
                MessageBox.Show("No puedes eliminar el perfil activo. Cambia de perfil primero.", "Bloqueado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var toRemove = _editingProfile;
            _profiles.Remove(toRemove);
            App.Config.CurrentSettings.Profiles.Remove(toRemove);
        }
        else if (_profiles.Count <= 1)
        {
             MessageBox.Show("Debe existir al menos un perfil.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _editingProfile == null) return;
        _editingProfile.Name = ProfileNameBox.Text;
        // Refresh list display? ObservableCollection of objects doesn't auto-notify changes to props causing list item re-render unless using INotifyPropertyChanged.
        // Assuming ProfileModel implements it or we force refresh.
        // ProfilesListBox.Items.Refresh(); // Simple force
    }

    private void ProfileTriggerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _editingProfile == null) return;
        _editingProfile.TriggerAppPath = ProfileTriggerBox.Text;
    }

    private void BrowseTrigger_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
        };
        
        if (dialog.ShowDialog() == true)
        {
            // Just verify filename usually logic for ProcessMonitor
            // ProcessMonitor checks ProcessName (e.g. "photoshop") not full path normally, but getting full path or EXE name is fine.
            // Let's store just the EXE name for simplicity as per ProcessMonitor logic implemented.
            string exeName = System.IO.Path.GetFileName(dialog.FileName);
            ProfileTriggerBox.Text = exeName;
        }
    }

    private void EditProfileItems_Click(object sender, RoutedEventArgs e)
    {
        NavList.SelectedIndex = 2; // Go to Items
    }

    private void EditProfileAppearance_Click(object sender, RoutedEventArgs e)
    {
        NavList.SelectedIndex = 3; // Go to Appearance
    }

    private void DetectHotkey_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Detección de teclas no implementada aún en esta versión básica.", "Información");
    }

    // ===== SUBMENU NAVIGATION =====
    
    private void EditSubItems_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;
        
        // Initialize SubItems if null
        if (_selectedItem.SubItems == null)
        {
            _selectedItem.SubItems = new List<MenuItemModel>();
        }
        
        // Push current parent to stack (null means root level)
        _parentStack.Push(_currentItemsList == null ? null : _selectedItem);
        
        // Navigate to sub-items - this is a REFERENCE, not a copy
        _currentItemsList = _selectedItem.SubItems;
        ItemsListBox.ItemsSource = _currentItemsList;
        
        UpdateBreadcrumb();
        ItemsListBox.SelectedIndex = -1;
        ItemDetailsPanel.IsEnabled = false;
    }

    private void BackToParent_Click(object sender, RoutedEventArgs e)
    {
        if (_parentStack.Count == 0) return;
        
        // Pop to go back
        _parentStack.Pop();
        
        // If stack is empty, we're back at root
        if (_parentStack.Count == 0)
        {
            _currentItemsList = null;
            ItemsListBox.ItemsSource = _menuItems;
        }
        else
        {
            // Get the current parent's SubItems
            var currentParent = _parentStack.Peek();
            if (currentParent != null)
            {
                _currentItemsList = currentParent.SubItems;
                ItemsListBox.ItemsSource = _currentItemsList;
            }
            else
            {
                _currentItemsList = null;
                ItemsListBox.ItemsSource = _menuItems;
            }
        }
        
        UpdateBreadcrumb();
        ItemsListBox.SelectedIndex = -1;
        ItemDetailsPanel.IsEnabled = false;
    }

    private void UpdateBreadcrumb()
    {
        if (_parentStack.Count == 0)
        {
            CurrentLevelLabel.Text = "Nivel: Principal";
            BackToParentBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            CurrentLevelLabel.Text = $"Nivel: Submenú (profundidad {_parentStack.Count})";
            BackToParentBtn.Visibility = Visibility.Visible;
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new IconPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedIconName != null)
        {
            // Store the Fluent Icon symbol name
            ItemIconBox.Text = picker.SelectedIconName;
            
            if (_selectedItem != null)
            {
                _selectedItem.Icon = picker.SelectedIconName;
            }
        }
    }

    private void ItemIconBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem == null) return;
        
        _selectedItem.Icon = ItemIconBox.Text;
        UpdateIconPreview();
    }

    private async void UpdateIconPreview()
    {
        if (IconPreview == null) return;

        string iconName = ItemIconBox.Text;
        
        // System.Diagnostics.Debug.WriteLine($"UpdateIconPreview: {iconName}");

        if (!string.IsNullOrEmpty(iconName) && iconName.Contains(":"))
        {
             // Universal Icon
             IconPreview.Visibility = Visibility.Collapsed;
             UniversalIconPreview.Visibility = Visibility.Visible;
             
             try
             {
                 var service = new RadiMenu.Services.IconifyService();
                 var data = await service.GetIconDataAsync(iconName);
                 
                 if (data != null)
                 {
                     string pathData = service.ExtractPathData(data.Body);
                     if (!string.IsNullOrEmpty(pathData))
                     {
                         UniversalIconPreview.Data = System.Windows.Media.Geometry.Parse(pathData);
                     }
                 }
             }
             catch
             {
                UniversalIconPreview.Data = null;
             }
        }
        else
        {
            // Fluent Icon
            UniversalIconPreview.Visibility = Visibility.Collapsed;
            
            if (System.Enum.TryParse<FluentIcons.Common.Symbol>(iconName, out var symbol))
            {
                IconPreview.Symbol = symbol;
                IconPreview.Visibility = Visibility.Visible;
            }
            else
            {
                IconPreview.Symbol = FluentIcons.Common.Symbol.Question;
                IconPreview.Visibility = string.IsNullOrEmpty(iconName) ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }
}
