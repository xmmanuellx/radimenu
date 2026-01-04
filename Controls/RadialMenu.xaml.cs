using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MenuItemModel = RadiMenu.Models.MenuItem;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace RadiMenu.Controls;

public partial class RadialMenu : System.Windows.Controls.UserControl
{
    private List<MenuItemModel> _items = new();
    private List<RadialMenuItem> _menuItemControls = new();
    private int _selectedIndex = -1;
    
    private double _currentIconRadius = 105;
    private const double CenterX = 250;  
    private const double CenterY = 250;
    private const double ItemSize = 36;
    
    private double _minHitRadius = 25;  
    private double _maxHitRadius = 110; // Default to match Visual size (25+85), NOT 140 to avoid submenu overlap 
    
    public event EventHandler<MenuItemModel>? ItemSelected;
    
    private Point _lastMousePos;
    private long _lastMouseTime;
    private long _menuOpenTime;
    private bool _gestureActivated = false;
    
    // Timer-based gesture detection
    private DispatcherTimer? _gestureTimer;
    private Point _menuCenterScreen;
    
    // Submenu navigation
    private Stack<List<MenuItemModel>> _menuStack = new();
    private DispatcherTimer? _hoverTimer;
    private int _hoveredIndex = -1;
    private int _currentDepth = 0;
    
    // Nested submenu (outer ring)
    private List<MenuItemModel>? _submenuItems;
    private List<RadialMenuItem> _submenuItemControls = new();
    private int _parentItemIndex = -1; // Index of the parent item in main menu
    private int _hoveredSubmenuIndex = -1;
    private Path? _submenuHighlight; // Highlight for hovered submenu item
    private bool _wasInCenter = false; // Track if mouse was in center
    
    // Modular Drag Manager (replaces scattered drag flags)
    private readonly DragManager _dragManager = new();
    
    // Legacy flags (kept temporarily for compatibility during migration)
    private bool _mouseIsDown = false;
    private Point _mouseDownPoint;
    
    public RadialMenu()
    {
        InitializeComponent();
        _lastMouseTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        _menuOpenTime = _lastMouseTime;
        LoadDefaultItems();
        ApplyAppearance();
        RenderItems();
        
        RootGrid.MouseMove += RootGrid_MouseMove;
        RootGrid.MouseDown += RootGrid_MouseDown;
        RootGrid.MouseUp += RootGrid_MouseUp;
        
        // Wire DragManager events
        _dragManager.DragActivated += OnDragActivated;
        _dragManager.ItemsSwapped += OnItemsSwapped;
        _dragManager.DragFinished += OnDragFinished;
        _dragManager.DragCanceled += OnDragCanceled;
    }

    private void LoadDefaultItems()
    {
        // Load from Config
        if (App.Config?.ActiveProfile?.MenuItems != null)
        {
            _items = App.Config.ActiveProfile.MenuItems;
        }
        else
        {
            // Fallback if config failed
             _items = new List<MenuItemModel>
            {
                new() { Label = "Volume", Icon = "Speaker2" },
                new() { Label = "Undo", Icon = "ArrowUndo" },
                new() { Label = "Zoom", Icon = "ZoomIn" },
                new() { Label = "Scroll", Icon = "ArrowSort" },
                new() { Label = "Brightness", Icon = "BrightnessHigh" },
            };
        }
    }

    public void ReloadItems()
    {
        LoadDefaultItems();
        ApplyAppearance(); // Updates sizes first
        RenderItems(); // Uses updated sizes
    }
    
    /// <summary>
    /// Ensures the menu is showing the main profile items, not config mode.
    /// Call this every time the menu is shown.
    /// </summary>
    public void EnsureMainMenu()
    {
        if (_isConfigMode)
        {
            _isConfigMode = false;
            LoadDefaultItems();
            RenderItems();
            ApplyAppearance();
        }
        
        // Also close any open submenus
        if (_submenuItems != null)
        {
            CloseSubmenu();
        }
        
        // Reset selection state
        _selectedIndex = -1;
        _hoveredSubmenuIndex = -1;
        _wasInCenter = false;
        
        // Reset drag state via DragManager
        _dragManager.CancelDrag();
        _mouseIsDown = false;
    }

    public void ResetGestureTimer()
    {
        _menuOpenTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        _gestureActivated = false;
        
        // Start timer-based gesture detection if enabled
        if (App.Config.CurrentSettings.EnableGestureActivation)
        {
            StartGestureTimer();
        }
    }

    private void StartGestureTimer()
    {
        // Calculate menu center in screen coordinates
        try
        {
            _menuCenterScreen = this.PointToScreen(new Point(CenterX, CenterY));
        }
        catch
        {
            // If not attached to visual tree yet, use a fallback
            return;
        }
        
        // Create and start the timer
        _gestureTimer?.Stop();
        _gestureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        _gestureTimer.Tick += GestureTimer_Tick;
        _gestureTimer.Start();
    }

    private void StopGestureTimer()
    {
        if (_gestureTimer != null)
        {
            _gestureTimer.Stop();
            _gestureTimer.Tick -= GestureTimer_Tick;
            _gestureTimer = null;
        }
    }

    private void GestureTimer_Tick(object? sender, EventArgs e)
    {
        // Get current mouse position in screen coordinates
        var mouseScreenPos = RadiMenu.Services.InputService.GetMousePosition();
        
        // Calculate distance from menu center
        double dx = mouseScreenPos.X - _menuCenterScreen.X;
        double dy = mouseScreenPos.Y - _menuCenterScreen.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        
        // Get DPI scaling to convert _maxHitRadius to screen pixels
        var source = PresentationSource.FromVisual(this);
        double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double maxRadiusScreen = _maxHitRadius * dpiScale;
        
        // Update selected index based on angle (even before crossing radius)
        // But only if mouse is within main ring (not in submenu area)
        double minRadiusScreen = _minHitRadius * dpiScale;
        bool inMainRing = distance >= minRadiusScreen && distance <= maxRadiusScreen;
        
        if (distance > 20 * dpiScale && (inMainRing || _submenuItems == null))
        {
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle < 0) angle += 360;
            
            double rotatedAngle = angle + 90;
            if (rotatedAngle >= 360) rotatedAngle -= 360;
            
            int count = _items.Count;
            if (count > 0)
            {
                double step = 360.0 / count;
                double shiftedAngle = rotatedAngle + (step / 2);
                if (shiftedAngle >= 360) shiftedAngle -= 360;
                
                int index = (int)(shiftedAngle / step);
                if (index >= 0 && index < count && index != _selectedIndex)
                {
                    SelectItem(index);
                }
            }
        }
        
        // Check if crossed the outer radius
        if (distance > maxRadiusScreen && !_gestureActivated && _selectedIndex != -1)
        {
            long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            double elapsed = currentTime - _menuOpenTime;
            double timeLimit = App.Config.CurrentSettings.GestureTimeLimit;
            const double minWarmupTime = 100.0;
            
            // If within time window, activate!
            if (elapsed > minWarmupTime && elapsed < timeLimit)
            {
                _gestureActivated = true;
                StopGestureTimer();
                
                var item = _items[_selectedIndex];
                
                // If submenu-only, open submenu instead
                if (item.IsSubmenuOnly)
                {
                    OpenSubmenu(item);
                }
                else
                {
                    ItemSelected?.Invoke(this, item);
                }
            }
        }
    }

    public void ReleaseGestureCapture()
    {
        StopGestureTimer();
        StopHoverTimer();
        ResetSubmenuStack();
    }

    // ===== SUBMENU NAVIGATION =====
    
    private void ResetSubmenuStack()
    {
        _menuStack.Clear();
        _currentDepth = 0;
        _hoveredIndex = -1;
    }

    public void OpenSubmenu(MenuItemModel parentItem)
    {
        if (!parentItem.HasSubItems) return;
        
        // Always close any existing submenu elements first to prevent stacking/darkening
        CloseSubmenu();
        
        int maxDepth = App.Config.CurrentSettings.MaxSubmenuDepth;
        if (_currentDepth >= maxDepth) return;
        
        // Find the parent item index
        int parentIndex = _items.IndexOf(parentItem);
        if (parentIndex < 0) return;
        
        // Store submenu data
        _submenuItems = parentItem.SubItems!.ToList();
        _parentItemIndex = parentIndex;
        _currentDepth++;
        
        // Render the submenu as an outer ring
        RenderSubmenuRing();
        
        // Trigger animation
        AnimateSubmenuIn();
    }

    public void CloseSubmenu()
    {
        // Clear submenu state
        _submenuItems = null;
        _parentItemIndex = -1;
        _currentDepth = 0;
        _hoveredSubmenuIndex = -1;
        _submenuItemControls.Clear();
        
        // Remove submenu highlight
        if (_submenuHighlight != null)
        {
            ItemsCanvas.Children.Remove(_submenuHighlight);
            _submenuHighlight = null;
        }
        
        // Always try to remove submenu elements from canvas by Tag
        var elements = ItemsCanvas.Children.OfType<FrameworkElement>()
            .Where(e => e.Tag?.ToString() == "submenu").ToList();
            
        foreach (var el in elements)
        {
            ItemsCanvas.Children.Remove(el);
        }
        
        // Restore indicator geometry to main menu size
        if (_items.Count > 0)
        {
            double sweepAngle = 360.0 / _items.Count;
            double outerR = MainRing.Width / 2;
            double indicatorThickness = App.Config?.ActiveProfile?.Appearance?.IndicatorThickness ?? 4;
            
            UpdateArcGeometry(SelectionIndicator, sweepAngle, outerR, indicatorThickness);
            SectorHighlight.Visibility = Visibility.Visible;
        }
    }

    public void GoBack()
    {
        // If we have a submenu open, close it
        if (_submenuItems != null)
        {
            CloseSubmenu();
            return;
        }
        
        // Otherwise do nothing (we're at root)
    }

    public bool CanGoBack => _menuStack.Count > 0;

    private void AnimateSubmenuIn()
    {
        // Quick scale animation for submenu transition
        var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
    }

    private void RenderSubmenuRing()
    {
        if (_submenuItems == null || _submenuItems.Count == 0) return;
        
        // CLEANUP: Remove ANY existing submenu elements to prevent stacking
        var existingElements = ItemsCanvas.Children.OfType<FrameworkElement>()
            .Where(e => e.Tag?.ToString() == "submenu").ToList();
        foreach (var el in existingElements)
        {
            ItemsCanvas.Children.Remove(el);
        }
        _submenuItemControls.Clear();

        // FULL 360 LOGIC: Submenu always takes full circle
        // Items distributed equally starting from top (-90 degrees)
        double subAngleStep = 360.0 / _submenuItems.Count;
        double subStartDraw = -90; // Top
        
        double innerR = CenterCircle.Width / 2;
        double thickness = App.Config?.ActiveProfile?.Appearance?.RingThickness ?? 85;
        double outerRingStart = innerR + thickness;
        double outerRingThickness = thickness; // Same as main ring
        double submenuIconRadius = outerRingStart + (outerRingThickness / 2);
        
        // Render background arc (Full 360)
        RenderSubmenuArc(subStartDraw, 360.0, outerRingStart, outerRingThickness);
        
        // Render icons
        for (int i = 0; i < _submenuItems.Count; i++)
        {
            var item = _submenuItems[i];
            
            // Calculate center of this specific item's wedge
            // FIX: Remove half-step offset to align with Top-Centered logic
            double itemAngle = subStartDraw + (i * subAngleStep);
            
            var menuItemControl = new RadialMenuItem
            {
                DataContext = item,
                Width = 40,
                Height = 40,
                IsHitTestVisible = false,
                Tag = "submenu"
            };
            
            double angleRad = itemAngle * Math.PI / 180;
            double x = CenterX + (submenuIconRadius * Math.Cos(angleRad)) - 20;
            double y = CenterY + (submenuIconRadius * Math.Sin(angleRad)) - 20;
            
            Canvas.SetLeft(menuItemControl, x);
            Canvas.SetTop(menuItemControl, y);
            
            ItemsCanvas.Children.Add(menuItemControl);
            _submenuItemControls.Add(menuItemControl);
        }
        
        SelectSubmenuItem(-1);
    }

    private void RenderSubmenuArc(double startAngle, double sweepAngle, double radius, double thickness)
    {
        var arcPath = new Path
        {
            Fill = MainRing.Fill,
            Tag = "submenu",
            IsHitTestVisible = false
        };
        
        double outerRadius = radius + thickness;
        double innerRadius = radius;

        // HANDLE FULL CIRCLE (Donut)
        // If sweep is ~360, use EllipseGeometry to avoid ArcSegment glitches
        if (sweepAngle >= 359.9)
        {
             var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
             
             // Outer Circle
             var outer = new EllipseGeometry(new Point(CenterX, CenterY), outerRadius, outerRadius);
             geometry.AddGeometry(outer);
             
             // Inner Circle (Hole)
             var inner = new EllipseGeometry(new Point(CenterX, CenterY), innerRadius, innerRadius);
             geometry.AddGeometry(inner);
             
             arcPath.Data = geometry;
        }
        else
        {
            // Standard Arc Logic (Fallback)
            double startRad = startAngle * Math.PI / 180;
            double endRad = (startAngle + sweepAngle) * Math.PI / 180;
            
            Point outerStart = new Point(CenterX + outerRadius * Math.Cos(startRad), CenterY + outerRadius * Math.Sin(startRad));
            Point outerEnd = new Point(CenterX + outerRadius * Math.Cos(endRad), CenterY + outerRadius * Math.Sin(endRad));
            Point innerEnd = new Point(CenterX + innerRadius * Math.Cos(endRad), CenterY + innerRadius * Math.Sin(endRad));
            Point innerStart = new Point(CenterX + innerRadius * Math.Cos(startRad), CenterY + innerRadius * Math.Sin(startRad));
            
            bool isLargeArc = sweepAngle > 180;
            
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
            figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(innerEnd, true));
            figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));
            geometry.Figures.Add(figure);
            
            arcPath.Data = geometry;
        }
        
        // Insert at beginning so it's behind items
        ItemsCanvas.Children.Insert(0, arcPath);
    }

    
    private void StartHoverTimer(int itemIndex)
    {
        if (_hoveredIndex == itemIndex) return;
        
        StopHoverTimer();
        _hoveredIndex = itemIndex;
        
        // Only start timer if the item has a submenu
        var item = _items[itemIndex];
        if (!item.HasSubItems) return;
        
        long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        double elapsed = currentTime - _menuOpenTime;
        double gestureTimeLimit = App.Config.CurrentSettings.GestureTimeLimit;
        
        // Calculate delay - either wait for gesture time limit + hover delay, or just hover delay
        double delay;
        if (elapsed < gestureTimeLimit)
        {
            // Not yet in exploration mode - wait until we are, then add hover delay
            delay = (gestureTimeLimit - elapsed) + App.Config.CurrentSettings.SubmenuHoverDelay;
        }
        else
        {
            // Already in exploration mode - just use hover delay
            delay = App.Config.CurrentSettings.SubmenuHoverDelay;
        }
        
        _hoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delay)
        };
        _hoverTimer.Tick += HoverTimer_Tick;
        _hoverTimer.Start();
    }

    private void StopHoverTimer()
    {
        if (_hoverTimer != null)
        {
            _hoverTimer.Stop();
            _hoverTimer.Tick -= HoverTimer_Tick;
            _hoverTimer = null;
        }
        _hoveredIndex = -1;
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        // Guard: Do not open submenu if interaction is locked
        if (_isContextMenuOpen || _isDialogOpen)
        {
            StopHoverTimer();
            return;
        }

        // Save the index BEFORE stopping the timer (which resets _hoveredIndex)
        int indexToOpen = _hoveredIndex;
        StopHoverTimer();
        
        if (indexToOpen >= 0 && indexToOpen < _items.Count)
        {
            var item = _items[indexToOpen];
            if (item.HasSubItems)
            {
                OpenSubmenu(item);
            }
        }
    }

    private void ApplyAppearance()
    {
        if (App.Config?.ActiveProfile?.Appearance != null)
        {
            var settings = App.Config.ActiveProfile.Appearance;
            
            try
            {
                // Colors
                var bgBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(settings.BackgroundColor);
                MainRing.Fill = bgBrush;
                
                var accentBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(settings.AccentColor);
                SelectionIndicator.Stroke = accentBrush;
                SelectionIndicator.StrokeThickness = settings.IndicatorThickness;
                
                // --- Dynamic Sizing based on Independent Center Radius and Ring Thickness ---
                
                double centerRadius = settings.CenterRadius; // User defined hole radius
                double ringThickness = settings.RingThickness; // User defined band thickness
                
                // Outer Radius = Center + Ring
                double outerRadius = centerRadius + ringThickness;
                double innerRadius = centerRadius;
                
                // Update MainRing (Black Background)
                // Width = OuterRadius * 2
                MainRing.Width = outerRadius * 2;
                MainRing.Height = outerRadius * 2;
                
                // Update Shadow (slightly larger)
                // Assuming Shadow is currently 270 vs Ring 260 (+10)
                var shadowEllipse = (Ellipse)RootGrid.Children[1]; // Index 1 is shadow based on XAML
                shadowEllipse.Width = (outerRadius * 2) + 10;
                shadowEllipse.Height = (outerRadius * 2) + 10;
                
                // Update CenterCircle (The hole)
                CenterCircle.Width = innerRadius * 2;
                CenterCircle.Height = innerRadius * 2;
                
                // Update Icon Placement Radius
                // Icons centered in band
                _currentIconRadius = innerRadius + (ringThickness / 2);
                
                // Update Hit Test Radii
                _minHitRadius = innerRadius;
                _maxHitRadius = outerRadius;
                
                // Force geometry update with new dimensions
                UpdateGeometries(360.0 / (_items.Count > 0 ? _items.Count : 1));
                
                System.Diagnostics.Debug.WriteLine($"[ApplyAppearance] Updated radii: Min={_minHitRadius}, Max={_maxHitRadius}");
            }
            catch (Exception ex)
            { 
                System.Diagnostics.Debug.WriteLine($"[ApplyAppearance] Error: {ex.Message}");
            }
        }
    }

    public void SetItems(List<MenuItemModel> items)
    {
        _items = items;
        RenderItems();
    }

    private void RenderItems()
    {
        ItemsCanvas.Children.Clear();
        _menuItemControls.Clear();
        
        if (_items.Count == 0) return;
        
        double sectorAngle = 360.0 / _items.Count;
        
        // Update both Arc and Sector geometries
        // Ensure Appearance is applied FIRST so sizes are correct
        ApplyAppearance(); 

        // Update Arc/Sector
        UpdateGeometries(sectorAngle);

        // Calculate Icon Radius based on current MainRing and CenterCircle
        // Inner = CenterCircle.Width / 2
        // RingThickness implies Outer = Inner + Thickness
        // IconRadius is middle of that ring: Inner + (Thickness/2)
        
        double innerR = CenterCircle.Width / 2;
        double thickness = 85; 
        if (App.Config?.ActiveProfile?.Appearance != null)
        {
            thickness = App.Config.ActiveProfile.Appearance.RingThickness;
        }
        _currentIconRadius = innerR + (thickness / 2);

        double angleStep = 360.0 / _items.Count;
        double startAngle = -90; 
        
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            item.Angle = startAngle + (i * angleStep);
            
            var menuItemControl = new RadialMenuItem
            {
                DataContext = item,
                Width = ItemSize,
                Height = ItemSize,
                IsHitTestVisible = false 
            };
            
            double angleRad = item.Angle * Math.PI / 180;
            double x = CenterX + (_currentIconRadius * Math.Cos(angleRad)) - (ItemSize / 2);
            double y = CenterY + (_currentIconRadius * Math.Sin(angleRad)) - (ItemSize / 2);
            
            Canvas.SetLeft(menuItemControl, x);
            Canvas.SetTop(menuItemControl, y);
            
            ItemsCanvas.Children.Add(menuItemControl);
            _menuItemControls.Add(menuItemControl);
        }
        
        if (_items.Count > 0)
        {
            SelectItem(0, triggerHover: false);
        }
    }
    
    private void UpdateGeometries(double sweepAngle)
    {
        // Recalculate inner radius based on current CenterCircle
        // CenterCircle Width = InnerRadius * 2
        double innerRadius = CenterCircle.Width / 2;
        // Outer Radius is determined by MainRing
        double outerRadius = MainRing.Width / 2;
        
        // Arc Radius for Blue Line
        // Currently drawn at "outer edge". If outerRadius changed, this should follow.
        // User originally asked for outer edge 130.
        // Let's stick to Outer Edge = outerRadius.
        double arcRadius = outerRadius;
        
        double thickness = 4;
        if (App.Config?.ActiveProfile?.Appearance != null)
        {
            thickness = App.Config.ActiveProfile.Appearance.IndicatorThickness;
        }
        
        UpdateArcGeometry(SelectionIndicator, sweepAngle, arcRadius, thickness);
        
        // Sector Highlight geometry
        UpdateSectorGeometry(SectorHighlight, sweepAngle, innerRadius, outerRadius);
    }
    
    private void UpdateArcGeometry(Path path, double sweepAngle, double radius, double thickness)
    {
        double startAngle = -sweepAngle / 2;
        double endAngle = sweepAngle / 2;
        
        double startRad = (startAngle - 90) * Math.PI / 180.0;
        double endRad = (endAngle - 90) * Math.PI / 180.0;
        
        double startX = radius * Math.Cos(startRad);
        double startY = radius * Math.Sin(startRad);
        
        double endX = radius * Math.Cos(endRad);
        double endY = radius * Math.Sin(endRad);
        
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(startX, startY) };
        
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(endX, endY),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepAngle > 180
        });
        
        geometry.Figures.Add(figure);
        path.Data = geometry;
    }
    
    private void UpdateSectorGeometry(Path path, double sweepAngle, double innerRadius, double outerRadius)
    {
        // Donut slice
        double startAngle = -(sweepAngle - 2) / 2; // -2 for small gap
        double endAngle = (sweepAngle - 2) / 2;
        
        double startRad = (startAngle - 90) * Math.PI / 180.0;
        double endRad = (endAngle - 90) * Math.PI / 180.0;
        
        // Outer Arc Start/End
        double outerStartX = outerRadius * Math.Cos(startRad);
        double outerStartY = outerRadius * Math.Sin(startRad);
        double outerEndX = outerRadius * Math.Cos(endRad);
        double outerEndY = outerRadius * Math.Sin(endRad);
        
        // Inner Arc Start/End
        double innerStartX = innerRadius * Math.Cos(startRad);
        double innerStartY = innerRadius * Math.Sin(startRad);
        double innerEndX = innerRadius * Math.Cos(endRad);
        double innerEndY = innerRadius * Math.Sin(endRad);
        
        var geometry = new PathGeometry();
        
        // Draw the shape:
        var figure = new PathFigure { StartPoint = new Point(innerStartX, innerStartY), IsClosed = true };
        
        figure.Segments.Add(new LineSegment { Point = new Point(outerStartX, outerStartY) });
        figure.Segments.Add(new ArcSegment 
        { 
            Point = new Point(outerEndX, outerEndY), 
            Size = new Size(outerRadius, outerRadius), 
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepAngle > 180
        });
        figure.Segments.Add(new LineSegment { Point = new Point(innerEndX, innerEndY) });
        figure.Segments.Add(new ArcSegment 
        { 
            Point = new Point(innerStartX, innerStartY), 
            Size = new Size(innerRadius, innerRadius), 
            SweepDirection = SweepDirection.Counterclockwise, // Corrected casing
            IsLargeArc = sweepAngle > 180
        });
        
        geometry.Figures.Add(figure);
        path.Data = geometry;
    }

    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        // Block interaction if context menu OR dialog is open
        if (_isContextMenuOpen || _isDialogOpen) return;

        var mousePos = e.GetPosition(RootGrid);
        double dx = mousePos.X - CenterX;
        double dy = mousePos.Y - CenterY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        
        int count = _items.Count;
        if (count == 0) return;
        double step = 360.0 / count;

        // 1. Calculate angles once
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (angle < 0) angle += 360;
        double rotatedAngle = angle + 90;
        if (rotatedAngle >= 360) rotatedAngle -= 360;
        
        // --- DRAG HANDLING VIA DRAGMANAGER ---
        if (_dragManager.IsActive)
        {
            // Try to activate drag if pending
            _dragManager.TryActivateDrag(mousePos);
            
            // If actively dragging, update target
            if (_dragManager.IsDragging)
            {
                int targetIndex = -1;
                
                if (_dragManager.Target == DragManager.DragTarget.MainMenu && distance >= _minHitRadius && distance <= _maxHitRadius)
                {
                    double shiftedAngle = rotatedAngle + (step / 2);
                    while (shiftedAngle >= 360) shiftedAngle -= 360;
                    targetIndex = (int)(shiftedAngle / step);
                    if (targetIndex < 0 || targetIndex >= count) targetIndex = -1;
                }
                else if (_dragManager.Target == DragManager.DragTarget.Submenu && _submenuItems != null)
                {
                    double subStep = 360.0 / _submenuItems.Count;
                    double shiftedAngle = rotatedAngle + (subStep / 2);
                    while (shiftedAngle >= 360) shiftedAngle -= 360;
                    targetIndex = (int)(shiftedAngle / subStep);
                    if (targetIndex < 0 || targetIndex >= _submenuItems.Count) targetIndex = -1;
                }
                
                if (targetIndex >= 0)
                {
                    _dragManager.UpdateDragTarget(targetIndex);
                }
                
                return; // Don't process hover while dragging
            }
        }
        // --- END DRAG ---


        // 2. Handle Submenu Priority
        if (_submenuItems != null)
        {
            // Increased range (300px) to allow easier hovering without being "Infinite"
            if (distance > _maxHitRadius + 300 || distance < _minHitRadius - 10)
            {
                CloseSubmenu();
            }
            else if (distance > _maxHitRadius)
            {
                _lastMousePos = mousePos;

                // FULL 360 HOVER LOGIC (CENTER ALIGNED)
                double subStep = 360.0 / _submenuItems.Count;
                
                // Shift by half step to align with Top-Centered items
                // This matches RootGrid_MouseUp logic
                double shiftedAngle = rotatedAngle + (subStep / 2);
                while (shiftedAngle >= 360) shiftedAngle -= 360;
                
                int subIndex = (int)(shiftedAngle / subStep);
                
                if (subIndex < 0) subIndex = 0;
                if (subIndex >= _submenuItems.Count) subIndex = _submenuItems.Count - 1;
                
                if (subIndex != _hoveredSubmenuIndex)
                    {
                        SelectSubmenuItem(subIndex);
                    }

                return;
            }
        }

        // 3. Main Menu Selection (if in main ring)
        // Note: If submenu is open and user moves to main ring, SelectItem will close submenu
        if (distance >= _minHitRadius && distance <= _maxHitRadius)
        {
            double shiftedAngle = rotatedAngle + (step / 2);
            while (shiftedAngle >= 360) shiftedAngle -= 360;
            
            int index = (int)(shiftedAngle / step);
            
            bool cameFromCenter = _wasInCenter;
            _wasInCenter = false;
            
            if (index >= 0 && index < count)
            {
                if (index != _selectedIndex)
                {
                    SelectItem(index);
                }
                else if (cameFromCenter && _items[index].HasSubItems)
                {
                    // Same item but came from center - open submenu instantly
                    OpenSubmenu(_items[index]);
                }
            }
        }
        else if (distance < _minHitRadius)
        {
            _wasInCenter = true;
        }
        
        // 4. Gesture Logic
        if (_submenuItems == null && distance > _maxHitRadius)
        {
             ProcessGestureActivation(distance);
        }

        _lastMousePos = mousePos;
        _lastMouseTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
    }

    private void ProcessGestureActivation(double distance)
    {
         if (App.Config.CurrentSettings.EnableGestureActivation && _selectedIndex != -1 && !_gestureActivated)
         {
             long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
             double elapsed = currentTime - _menuOpenTime;
             double timeLimit = App.Config.CurrentSettings.GestureTimeLimit;
             if (elapsed > 100.0 && elapsed < timeLimit)
             {
                 _gestureActivated = true;
                 var item = _items[_selectedIndex];
                 ReleaseGestureCapture();
                 ItemSelected?.Invoke(this, item);
             }
         }
    }
    
    private void SelectSubmenuItem(int index)
    {
        _hoveredSubmenuIndex = index;
        
        double innerR = CenterCircle.Width / 2;
        double ringThickness = App.Config?.ActiveProfile?.Appearance?.RingThickness ?? 85;
        double outerR = MainRing.Width / 2;
        double indicatorThickness = App.Config?.ActiveProfile?.Appearance?.IndicatorThickness ?? 4;
        
        if (index >= 0 && _submenuItems != null)
        {
            // FULL 360 HIGHLIGHT LOGIC
            double subStep = 360.0 / _submenuItems.Count;
            
            double outerRingStart = innerR + ringThickness;
            double outerRingThickness = ringThickness; // Same as main ring
            double indicatorRadius = outerRingStart + outerRingThickness;
            
            UpdateArcGeometry(SelectionIndicator, subStep, indicatorRadius, indicatorThickness);
            SectorHighlight.Visibility = Visibility.Collapsed;
            
            // --- Create/Update Submenu Highlight ---
            if (_submenuHighlight == null)
            {
                _submenuHighlight = new Path
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                    Tag = "submenu_highlight",
                    IsHitTestVisible = false
                };
                ItemsCanvas.Children.Add(_submenuHighlight);
            }
            
            // Update highlight geometry (donut slice for the hovered sub-item)
            UpdateSubmenuHighlightGeometry(_submenuHighlight, subStep, outerRingStart, outerRingThickness);
            
            // Position the highlight at the correct angle
            // Visual Top is 0, drawing -90. Highlight rotation is Visual.
            // FIX: Use Start Angle (index * step), NOT Center Angle, because RotateTransform rotates the Start of the arc.
            double targetAngle = index * subStep;
            
            _submenuHighlight.RenderTransformOrigin = new System.Windows.Point(0, 0);
            Canvas.SetLeft(_submenuHighlight, CenterX);
            Canvas.SetTop(_submenuHighlight, CenterY);
            _submenuHighlight.RenderTransform = new RotateTransform(targetAngle);
            _submenuHighlight.Visibility = Visibility.Visible;
            
            // Animate with shortest path
            AnimateToAngle(targetAngle);
            
            SelectionLabel.Text = _submenuItems[index].Label;
        }
        else
        {
            // --- Hide Submenu Highlight ---
            if (_submenuHighlight != null)
            {
                _submenuHighlight.Visibility = Visibility.Collapsed;
            }
            
            double sweepAngle = 360.0 / (_items.Count > 0 ? _items.Count : 1);
            UpdateArcGeometry(SelectionIndicator, sweepAngle, outerR, indicatorThickness);
            SectorHighlight.Visibility = Visibility.Visible;
            
            if (_selectedIndex >= 0)
            {
                double restoredAngle = _selectedIndex * (360.0 / _items.Count);
                AnimateToAngle(restoredAngle);
                SelectionLabel.Text = _items[_selectedIndex].Label;
            }
        }

        for (int i = 0; i < _submenuItemControls.Count; i++)
        {
            _submenuItemControls[i].SetSelected(i == index);
        }
    }
    
    private void AnimateToAngle(double targetAngle)
    {
        // Calculate shortest path from current rotation
        double diff = targetAngle - (_currentRotation % 360);
        
        // Handle negative current rotation
        if (_currentRotation < 0)
        {
            diff = targetAngle - ((_currentRotation % 360) + 360);
        }
        
        // Normalize diff to -180...180
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        
        // New target is current + shortest diff
        double newRotation = _currentRotation + diff;
        _currentRotation = newRotation;
        
        var animation = new DoubleAnimation(newRotation, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        SelectionRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
        SectorRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }
    
    private void UpdateSubmenuHighlightGeometry(Path path, double sweepAngle, double innerRadius, double thickness)
    {
        double outerRadius = innerRadius + thickness;
        double halfSweep = sweepAngle / 2;
        
        // Calculate angles (centered at 0, will be rotated by RenderTransform)
        double startAngle = -90 - halfSweep;
        double endAngle = -90 + halfSweep;
        
        double startRad = startAngle * Math.PI / 180;
        double endRad = endAngle * Math.PI / 180;
        
        Point outerStart = new Point(outerRadius * Math.Cos(startRad), outerRadius * Math.Sin(startRad));
        Point outerEnd = new Point(outerRadius * Math.Cos(endRad), outerRadius * Math.Sin(endRad));
        Point innerEnd = new Point(innerRadius * Math.Cos(endRad), innerRadius * Math.Sin(endRad));
        Point innerStart = new Point(innerRadius * Math.Cos(startRad), innerRadius * Math.Sin(startRad));
        
        bool isLargeArc = sweepAngle > 180;
        
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));
        geometry.Figures.Add(figure);
        
        path.Data = geometry;
    }
    
    private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        // Guard: Do not react if context menu is open
        if (_isContextMenuOpen || _isDialogOpen) return;

        // Cancel any active drag via DragManager
        if (_dragManager.IsActive)
        {
            _dragManager.CancelDrag();
        }

        // Clear legacy state
        _mouseIsDown = false;
        
        // Stop timers
        StopHoverTimer();
    }
    
    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isContextMenuOpen || _isDialogOpen) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        
        var mousePos = e.GetPosition(RootGrid);
        double dx = mousePos.X - CenterX;
        double dy = mousePos.Y - CenterY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        
        // Legacy for compatibility
        _mouseIsDown = true;
        _mouseDownPoint = mousePos;
        
        // Calculate angle
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (angle < 0) angle += 360;
        double rotatedAngle = angle + 90;
        while (rotatedAngle >= 360) rotatedAngle -= 360;
        
        // Check if in submenu ring
        if (_submenuItems != null && distance > _maxHitRadius)
        {
            double subStep = 360.0 / _submenuItems.Count;
            double shiftedAngle = rotatedAngle + (subStep / 2);
            while (shiftedAngle >= 360) shiftedAngle -= 360;
            int subIndex = (int)(shiftedAngle / subStep);
            
            if (subIndex >= 0 && subIndex < _submenuItems.Count)
            {
                _dragManager.BeginPotentialDrag(subIndex, mousePos, DragManager.DragTarget.Submenu);
                RootGrid.CaptureMouse();
            }
            return;
        }
        
        // Check if in main ring
        if (distance >= _minHitRadius && distance <= _maxHitRadius)
        {
            double step = 360.0 / _items.Count;
            double shiftedAngle = rotatedAngle + (step / 2);
            while (shiftedAngle >= 360) shiftedAngle -= 360;
            int index = (int)(shiftedAngle / step);
            
            if (index >= 0 && index < _items.Count)
            {
                _dragManager.BeginPotentialDrag(index, mousePos, DragManager.DragTarget.MainMenu);
                RootGrid.CaptureMouse();
            }
        }
    }
    
    private void RootGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isContextMenuOpen || _isDialogOpen) return;
        RootGrid.ReleaseMouseCapture();
         
         var mousePos = e.GetPosition(RootGrid);
         double dx = mousePos.X - CenterX;
         double dy = mousePos.Y - CenterY;
         double distance = Math.Sqrt(dx * dx + dy * dy);
         
         // DEBUG: Log all state at MouseUp
         System.Diagnostics.Debug.WriteLine($"[MouseUp] Btn={e.ChangedButton} distance={distance:F1}, _maxHitRadius={_maxHitRadius:F1}, submenu={_submenuItems != null}, inSubmenuZone={(distance > _maxHitRadius)}");
         
         // Fix for "Flick Execution": If we moved mouse > 10px, treat as DRAG even if _isDragging isn't set.
         // This handles cases where user cancels (leaves) then releases, or drags fast but _isDragging logic failed.
         double distMoved = (mousePos - _mouseDownPoint).Length;
         bool wasMoving = distMoved > 10;

         // --- LEFT CLICK HANDLER (Drag/Execute) ---
         if (e.ChangedButton == MouseButton.Left)
         {
             // If we were dragging, finish it
             if (_dragManager.IsDragging)
             {
                 _mouseIsDown = false;
                 _dragManager.FinishDrag();
                 return;
             }
             
             // If we moved significantly but drag didn't activate, cancel silently
             if (_dragManager.HasMovedSignificantly(mousePos))
             {
                 _mouseIsDown = false;
                 _dragManager.CancelDrag();
                 return;
             }
             
             // Reset state
             _mouseIsDown = false;
             
             // 1. Submenu Ring Click - calculate the clicked submenu item at click time
             if (_submenuItems != null && _submenuItems.Count > 0 && distance > _maxHitRadius)
             {
                 // FIX: Prioritize the item that is currently highlighted (hovered). 
                 if (_hoveredSubmenuIndex >= 0 && _hoveredSubmenuIndex < _submenuItems.Count)
                 {
                     var item = _submenuItems[_hoveredSubmenuIndex];
                     if (item.IsSubmenuOnly) OpenSubmenu(item);
                     else ItemSelected?.Invoke(this, item);
                     return;
                 }

                 // Fallback: Calculate angle
                 double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                 if (angle < 0) angle += 360;
                 double rotatedAngle = angle + 90; // Visual Angle (0=Top)
                 while (rotatedAngle >= 360) rotatedAngle -= 360;
                 
                 // FULL 360 CLICK LOGIC (CENTER ALIGNED)
                 double subStep = 360.0 / _submenuItems.Count;
                 
                 // Normalize to 0-360
                 while (rotatedAngle < 0) rotatedAngle += 360;
                 while (rotatedAngle >= 360) rotatedAngle -= 360;
                 
                 // Shift by half step to align hit zones with Top-Centered items
                 // This matches Main Menu logic
                 double shiftedAngle = rotatedAngle + (subStep / 2);
                 while (shiftedAngle >= 360) shiftedAngle -= 360;
                 
                 int subIndex = (int)(shiftedAngle / subStep);
                 
                 if (subIndex >= 0 && subIndex < _submenuItems.Count)
                 {
                     var item = _submenuItems[subIndex];
                     if (item.IsSubmenuOnly) OpenSubmenu(item);
                     else ItemSelected?.Invoke(this, item);
                     return;
                 }
             }

             // 2. Main Ring Click
             if (distance >= _minHitRadius && distance <= _maxHitRadius)
             {
                 if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                 {
                     var item = _items[_selectedIndex];
                     if (item.IsSubmenuOnly) OpenSubmenu(item);
                     else ItemSelected?.Invoke(this, item);
                 }
             }
             // Center Click
             else if (distance < _minHitRadius)
             {
                 // Check if we should skip this click (e.g. just toggled config mode)
                 if (_skipNextCenterClick)
                 {
                     _skipNextCenterClick = false;
                     return;
                 }

                 // Close menu or back
                 if (_submenuItems != null) CloseSubmenu();
                 else 
                 {
                     var window = Window.GetWindow(this) as MainWindow;
                     window?.HideMenu();
                 }
             }
         }
         
         // --- RIGHT CLICK HANDLER (Context Menu) ---
         else if (e.ChangedButton == MouseButton.Right)
         {
             // 1. Submenu Item Right Click
             if (_submenuItems != null && distance > _maxHitRadius)
             {
                 int subIndex = -1;
                 
                 if (_hoveredSubmenuIndex >= 0 && _hoveredSubmenuIndex < _submenuItems.Count)
                 {
                     subIndex = _hoveredSubmenuIndex;
                 }
                 // Fallback calc if needed (omitted for brevity, assume hover works well enough for context menu)
                 
                 if (subIndex >= 0)
                 {
                     ShowContextMenu(_submenuItems[subIndex], subIndex, true);
                     return;
                 }
             }
             
             // 2. Main Item Right Click
             if (distance >= _minHitRadius && distance <= _maxHitRadius)
             {
                 if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                 {
                     ShowContextMenu(_items[_selectedIndex], _selectedIndex, false);
                     return;
                 }
             }
             
             // 3. Center Right Click? (Maybe Global settings)
         }
    }
    
    private void StartDragFeedback(int index)
    {
        // Close any open submenu when starting drag
        if (_submenuItems != null)
        {
            CloseSubmenu();
        }
        
        // Visual feedback: scale up the dragged item
        if (index >= 0 && index < _menuItemControls.Count)
        {
            var control = _menuItemControls[index];
            var scaleTransform = new ScaleTransform(1.2, 1.2);
            control.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            control.RenderTransform = scaleTransform;
            control.Opacity = 0.8;
        }
        
        // Change label to indicate drag mode
        SelectionLabel.Text = "🔀 Arrastrando...";
    }
    
    private void SwapItems(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _items.Count) return;
        if (targetIndex < 0 || targetIndex >= _items.Count) return;
        if (sourceIndex >= _menuItemControls.Count || targetIndex >= _menuItemControls.Count) return;
        
        // Swap in the data list
        var tempItem = _items[sourceIndex];
        _items[sourceIndex] = _items[targetIndex];
        _items[targetIndex] = tempItem;
        
        // Get controls
        var sourceControl = _menuItemControls[sourceIndex];
        var targetControl = _menuItemControls[targetIndex];
        
        // Calculate the target positions based on INDEX (not reading from canvas)
        double angleStep = 360.0 / _items.Count;
        double startAngle = -90;
        
        double sourceTargetAngle = startAngle + (targetIndex * angleStep);
        double targetTargetAngle = startAngle + (sourceIndex * angleStep);
        
        double sourceTargetAngleRad = sourceTargetAngle * Math.PI / 180;
        double targetTargetAngleRad = targetTargetAngle * Math.PI / 180;
        
        double sourceNewX = CenterX + (_currentIconRadius * Math.Cos(sourceTargetAngleRad)) - (ItemSize / 2);
        double sourceNewY = CenterY + (_currentIconRadius * Math.Sin(sourceTargetAngleRad)) - (ItemSize / 2);
        double targetNewX = CenterX + (_currentIconRadius * Math.Cos(targetTargetAngleRad)) - (ItemSize / 2);
        double targetNewY = CenterY + (_currentIconRadius * Math.Sin(targetTargetAngleRad)) - (ItemSize / 2);
        
        // Animate to new positions (WPF will smoothly transition from current visual position)
        var duration = TimeSpan.FromMilliseconds(120);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        
        sourceControl.BeginAnimation(Canvas.LeftProperty, 
            new DoubleAnimation(sourceNewX, duration) { EasingFunction = easing });
        sourceControl.BeginAnimation(Canvas.TopProperty, 
            new DoubleAnimation(sourceNewY, duration) { EasingFunction = easing });
            
        targetControl.BeginAnimation(Canvas.LeftProperty, 
            new DoubleAnimation(targetNewX, duration) { EasingFunction = easing });
        targetControl.BeginAnimation(Canvas.TopProperty, 
            new DoubleAnimation(targetNewY, duration) { EasingFunction = easing });
        
        // Swap in the controls list
        _menuItemControls[sourceIndex] = targetControl;
        _menuItemControls[targetIndex] = sourceControl;
        
        // Keep drag feedback on the dragged item
        sourceControl.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        sourceControl.RenderTransform = new ScaleTransform(1.3, 1.3);
        sourceControl.Opacity = 0.9;
        
        // Reset other control's transform
        targetControl.RenderTransform = null;
        targetControl.Opacity = 1.0;
        
        // Update selection to follow dragged item
        _selectedIndex = targetIndex;
        AnimateToAngle(targetIndex * (360.0 / _items.Count));
    }
    
    private void StartSubmenuDragFeedback(int index)
    {
        // Visual feedback for submenu item
        if (index >= 0 && index < _submenuItemControls.Count)
        {
            var control = _submenuItemControls[index];
            control.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            control.RenderTransform = new ScaleTransform(1.2, 1.2);
            control.Opacity = 0.8;
        }
        
        SelectionLabel.Text = "🔀 Arrastrando...";
    }
    
    private void SwapSubmenuItems(int sourceIndex, int targetIndex)
    {
        if (_submenuItems == null) return;
        if (sourceIndex < 0 || sourceIndex >= _submenuItems.Count) return;
        if (targetIndex < 0 || targetIndex >= _submenuItems.Count) return;
        if (sourceIndex >= _submenuItemControls.Count || targetIndex >= _submenuItemControls.Count) return;
        
        // Swap in the data list
        var tempItem = _submenuItems[sourceIndex];
        _submenuItems[sourceIndex] = _submenuItems[targetIndex];
        _submenuItems[targetIndex] = tempItem;
        
        // Get controls
        var sourceControl = _submenuItemControls[sourceIndex];
        var targetControl = _submenuItemControls[targetIndex];
        
        // Calculate target positions for submenu items
        double parentAngle = 360.0 / _items.Count;
        double parentStartDraw = (_parentItemIndex * parentAngle) - 90;
        double subAngleStep = parentAngle / _submenuItems.Count;
        
        double innerR = CenterCircle.Width / 2;
        double thickness = App.Config?.ActiveProfile?.Appearance?.RingThickness ?? 85;
        double outerRingStart = innerR + thickness + 5;
        double outerRingThickness = thickness;
        double submenuIconRadius = outerRingStart + (outerRingThickness / 2);
        
        double sourceAngle = parentStartDraw + (targetIndex * subAngleStep) + (subAngleStep / 2);
        double targetAngle = parentStartDraw + (sourceIndex * subAngleStep) + (subAngleStep / 2);
        
        double sourceAngleRad = sourceAngle * Math.PI / 180;
        double targetAngleRad = targetAngle * Math.PI / 180;
        
        double sourceNewX = CenterX + (submenuIconRadius * Math.Cos(sourceAngleRad)) - 20;
        double sourceNewY = CenterY + (submenuIconRadius * Math.Sin(sourceAngleRad)) - 20;
        double targetNewX = CenterX + (submenuIconRadius * Math.Cos(targetAngleRad)) - 20;
        double targetNewY = CenterY + (submenuIconRadius * Math.Sin(targetAngleRad)) - 20;
        
        // Animate to new positions
        var duration = TimeSpan.FromMilliseconds(120);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        
        sourceControl.BeginAnimation(Canvas.LeftProperty, 
            new DoubleAnimation(sourceNewX, duration) { EasingFunction = easing });
        sourceControl.BeginAnimation(Canvas.TopProperty, 
            new DoubleAnimation(sourceNewY, duration) { EasingFunction = easing });
            
        targetControl.BeginAnimation(Canvas.LeftProperty, 
            new DoubleAnimation(targetNewX, duration) { EasingFunction = easing });
        targetControl.BeginAnimation(Canvas.TopProperty, 
            new DoubleAnimation(targetNewY, duration) { EasingFunction = easing });
        
        // Swap in the controls list
        _submenuItemControls[sourceIndex] = targetControl;
        _submenuItemControls[targetIndex] = sourceControl;
        
        // Keep drag feedback on dragged item
        sourceControl.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        sourceControl.RenderTransform = new ScaleTransform(1.2, 1.2);
        sourceControl.Opacity = 0.8;
        // Ensure ZIndex
        Canvas.SetZIndex(sourceControl, 10);
        
        targetControl.RenderTransform = null;
        targetControl.Opacity = 1.0;
        Canvas.SetZIndex(targetControl, 1);
        
        _hoveredSubmenuIndex = targetIndex;
        // FIX: Force visual update of the highlight/arc to match the new position
        SelectSubmenuItem(targetIndex);
    }

    // ========================================
    // DragManager Event Handlers
    // ========================================
    
    private void OnDragActivated(int index, DragManager.DragTarget target)
    {
        if (target == DragManager.DragTarget.MainMenu)
        {
            StartDragFeedback(index);
        }
        else if (target == DragManager.DragTarget.Submenu)
        {
            StartSubmenuDragFeedback(index);
        }
    }
    
    private void OnItemsSwapped(int sourceIndex, int targetIndex, DragManager.DragTarget target)
    {
        if (target == DragManager.DragTarget.MainMenu)
        {
            // Swap in data list
            var temp = _items[sourceIndex];
            _items[sourceIndex] = _items[targetIndex];
            _items[targetIndex] = temp;
            
            // Swap controls for visual animation
            var sourceControl = _menuItemControls[sourceIndex];
            var targetControl = _menuItemControls[targetIndex];
            _menuItemControls[sourceIndex] = targetControl;
            _menuItemControls[targetIndex] = sourceControl;
            
            // Animate positions
            AnimateSwap(_menuItemControls, sourceIndex, targetIndex, _items.Count);
        }
        else if (target == DragManager.DragTarget.Submenu && _submenuItems != null)
        {
            // Swap in data list
            var temp = _submenuItems[sourceIndex];
            _submenuItems[sourceIndex] = _submenuItems[targetIndex];
            _submenuItems[targetIndex] = temp;
            
            // Swap controls
            var sourceControl = _submenuItemControls[sourceIndex];
            var targetControl = _submenuItemControls[targetIndex];
            _submenuItemControls[sourceIndex] = targetControl;
            _submenuItemControls[targetIndex] = sourceControl;
            
            // Animate positions (submenu)
            AnimateSubmenuSwap(sourceIndex, targetIndex);
        }
    }
    
    private void OnDragFinished(DragManager.DragTarget target)
    {
        ClearDragVisuals(target);
        
        // Save to config
        if (App.Config?.ActiveProfile != null)
        {
            if (target == DragManager.DragTarget.Submenu && _parentItemIndex >= 0 && _submenuItems != null)
            {
                _items[_parentItemIndex].SubItems = _submenuItems;
            }
            App.Config.ActiveProfile.MenuItems = _items;
            App.Config.SaveSettings();
        }
        
        // Update label
        if (target == DragManager.DragTarget.MainMenu && _selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            SelectionLabel.Text = _items[_selectedIndex].Label;
        }
        else if (target == DragManager.DragTarget.Submenu && _hoveredSubmenuIndex >= 0 && _submenuItems != null && _hoveredSubmenuIndex < _submenuItems.Count)
        {
            SelectionLabel.Text = _submenuItems[_hoveredSubmenuIndex].Label;
        }
    }
    
    private void OnDragCanceled(DragManager.DragTarget target)
    {
        ClearDragVisuals(target);
    }
    
    private void ClearDragVisuals(DragManager.DragTarget target)
    {
        if (target == DragManager.DragTarget.MainMenu)
        {
            foreach (var control in _menuItemControls)
            {
                control.BeginAnimation(Canvas.LeftProperty, null);
                control.BeginAnimation(Canvas.TopProperty, null);
                control.RenderTransform = null;
                control.Opacity = 1.0;
            }
            RenderItems();
        }
        else if (target == DragManager.DragTarget.Submenu)
        {
            foreach (var control in _submenuItemControls)
            {
                control.BeginAnimation(Canvas.LeftProperty, null);
                control.BeginAnimation(Canvas.TopProperty, null);
                control.RenderTransform = null;
                control.Opacity = 1.0;
                Canvas.SetZIndex(control, 1);
            }
            RenderSubmenuRing();
        }
    }
    
    private void AnimateSwap(List<RadialMenuItem> controls, int idx1, int idx2, int totalCount)
    {
        double step = 360.0 / totalCount;
        double startAngle = -90;
        
        var duration = TimeSpan.FromMilliseconds(120);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        
        for (int i = 0; i < controls.Count; i++)
        {
            double angle = startAngle + (i * step);
            double rad = angle * Math.PI / 180;
            double x = CenterX + (_currentIconRadius * Math.Cos(rad)) - (ItemSize / 2);
            double y = CenterY + (_currentIconRadius * Math.Sin(rad)) - (ItemSize / 2);
            
            controls[i].BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, duration) { EasingFunction = easing });
            controls[i].BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, duration) { EasingFunction = easing });
        }
        
        // Keep drag feedback on dragged item
        if (_dragManager.CurrentIndex >= 0 && _dragManager.CurrentIndex < controls.Count)
        {
            var ctrl = controls[_dragManager.CurrentIndex];
            ctrl.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            ctrl.RenderTransform = new ScaleTransform(1.3, 1.3);
            ctrl.Opacity = 0.9;
        }
    }
    
    private void AnimateSubmenuSwap(int idx1, int idx2)
    {
        if (_submenuItems == null) return;
        
        double subStep = 360.0 / _submenuItems.Count;
        double subRadius = _maxHitRadius + 50;
        
        var duration = TimeSpan.FromMilliseconds(120);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        
        for (int i = 0; i < _submenuItemControls.Count; i++)
        {
            double angle = -90 + (i * subStep);
            double rad = angle * Math.PI / 180;
            double x = CenterX + (subRadius * Math.Cos(rad)) - (ItemSize / 2);
            double y = CenterY + (subRadius * Math.Sin(rad)) - (ItemSize / 2);
            
            _submenuItemControls[i].BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, duration) { EasingFunction = easing });
            _submenuItemControls[i].BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, duration) { EasingFunction = easing });
        }
        
        // Keep drag feedback
        if (_dragManager.CurrentIndex >= 0 && _dragManager.CurrentIndex < _submenuItemControls.Count)
        {
            var ctrl = _submenuItemControls[_dragManager.CurrentIndex];
            ctrl.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            ctrl.RenderTransform = new ScaleTransform(1.2, 1.2);
            ctrl.Opacity = 0.8;
            Canvas.SetZIndex(ctrl, 10);
        }
    }

    private double _currentRotation = 0;

    private void SelectItem(int index, bool triggerHover = true)
    {
        if (index < 0 || index >= _items.Count) return;
        
        // If we are changing selection, close any open submenu from the previous item
        if (_selectedIndex != index && _submenuItems != null)
        {
            CloseSubmenu();
        }

        _selectedIndex = index;
        var item = _items[index];
        SelectionLabel.Text = item.Label;
        
        // Open submenu instantly if item has subitems (only if requested)
        if (triggerHover && item.HasSubItems)
        {
            OpenSubmenu(item);
        }
        
        // Shortest path logic
        double step = 360.0 / _items.Count;
        double targetAngle = index * step;
        
        // Calculate difference respecting wrap-around
        double diff = targetAngle - (_currentRotation % 360);
        
        // Normalize diff to -180...180
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        
        // New target is current + shortest diff
        double newRotation = _currentRotation + diff;
        _currentRotation = newRotation;
        
        var animation = new DoubleAnimation(newRotation, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        SelectionRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
        SectorRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
        
        for (int i = 0; i < _menuItemControls.Count; i++)
        {
            _menuItemControls[i].SetSelected(i == index);
        }
    }

    private void ClickItem(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        
        var item = _items[index];
        
        // If submenu-only, open submenu instead of triggering action
        if (item.IsSubmenuOnly)
        {
            OpenSubmenu(item);
            return;
        }
        
        // Otherwise, trigger the action
        ItemSelected?.Invoke(this, item);
    }
    
    public void AnimateIn()
    {
        Opacity = 0;
        MenuScale.ScaleX = 0.9;
        MenuScale.ScaleY = 0.9;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var scaleIn = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fadeIn);
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
    }

    public void AnimateOut()
    {
        ReleaseGestureCapture();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        BeginAnimation(OpacityProperty, fadeOut);
    }

    public bool IsPointInMenu(Point point)
    {
        var center = new Point(CenterX, CenterY);
        var distance = Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2));
        
        // Base max radius (Main Menu)
        double limit = _maxHitRadius;
        
        // If submenu is open, extend the hit area
        if (_submenuItems != null)
        {
            // Add Submenu Ring Thickness + padding
            // Default: MainRing ~130 + 85 = 215
            // Safe upper bound: MainRing + 150
            limit += 150; 
        }
        
        return distance <= limit;
    }

    private void CenterCircle_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isContextMenuOpen || _isDialogOpen) return;
        // Logic to highlight center and deselect rings
        // ... highlight logic ...
        System.Diagnostics.Debug.WriteLine("Center Enter");
        SelectSubmenuItem(-1); // Deselect submenu items logic?
        
        // Actually CenterCircle logic:
        // Usually we deselect radial items or change cursor
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Hand;
    }
    
    private void CenterCircle_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isContextMenuOpen || _isDialogOpen) return;
        Mouse.OverrideCursor = null;
    }
    private bool _isConfigMode = false;
    private bool _skipNextCenterClick = false;
    
    // Custom Confirmation Dialog State
    private bool _isDialogOpen = false;
    private TaskCompletionSource<bool>? _dialogTcs;

    public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirmar")
    {
        // 1. Lock Interaction
        _isDialogOpen = true;
        
        // 2. Setup UI
        ConfirmationOverlay.Visibility = Visibility.Visible;
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        
        // 3. Create Task
        _dialogTcs = new TaskCompletionSource<bool>();
        
        // 4. Wait for user
        bool result = await _dialogTcs.Task;
        
        // 5. Cleanup
        ConfirmationOverlay.Visibility = Visibility.Collapsed;
        _isDialogOpen = false;
        _dialogTcs = null;
        
        return result;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        _dialogTcs?.TrySetResult(true);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _dialogTcs?.TrySetResult(false);
    }

    private void CenterCircle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isContextMenuOpen || _isDialogOpen) return;
        
        // Prevent event from bubbling to RootGrid if needed, 
        // but mainly we want to signal MouseUp to ignore the release action
        
        // If we're in a submenu, go back to parent menu
        if (CanGoBack)
        {
            GoBack();
            e.Handled = true;
            return;
        }
        
        // Otherwise toggle config mode
        ToggleConfigMode();
        
        // Flag to prevent RootGrid_MouseUp from processing this as a "Close Menu" or "Back" click
        // immediately after we switched modes.
        _skipNextCenterClick = true;
        e.Handled = true; 
    }

    private void ToggleConfigMode()
    {
        _isConfigMode = !_isConfigMode;
        
        if (_isConfigMode)
        {
            // Center Feedback (Visual change to indicate Config Mode?)
            // For now, relies on items changing.
            
            var configItems = GenerateConfigItems();
            SetItems(configItems);
        }
        else
        {
            // Restore Main Menu
            LoadDefaultItems();
            RenderItems();
            
            // Recalculate appearance just in case profile changed
            ApplyAppearance(); 
        }
    }

    private List<MenuItemModel> GenerateConfigItems()
    {
        var configItems = new List<MenuItemModel>();
        
        // 1. Profiles
        if (App.Config?.CurrentSettings?.Profiles != null)
        {
            foreach (var profile in App.Config.CurrentSettings.Profiles)
            {
                configItems.Add(new MenuItemModel 
                { 
                    Label = profile.Name,
                    Icon = "Person", // User icon
                    IsToggled = (profile.Id == App.Config.ActiveProfile.Id),
                    KeepOpen = true, // Don't close menu on switch
                    Action = () => SwitchProfile(profile.Id)
                });
            }
        }
        
        // 2. Pin Position Toggle
        bool isFixed = App.Config.ActiveProfile.PositionMode == RadiMenu.Models.PositionMode.Fixed;
        configItems.Add(new MenuItemModel
        {
            Label = isFixed ? "Fijo" : "Seguir",
            Icon = "Pin", // Pin icon
            IsToggled = isFixed,
            KeepOpen = true,
            Action = () => TogglePinPosition()
        });
        
        // 3. Settings Button
        configItems.Add(new MenuItemModel
        {
            Label = "Ajustes",
            Icon = "Settings", // Settings gear
            Action = () => OpenSettings()
        });
        
        return configItems;
    }

    private void SwitchProfile(Guid profileId)
    {
        if (App.Config == null) return;
        
        App.Config.CurrentSettings.ActiveProfileId = profileId;
        App.Config.SaveSettings();
        
        // Refresh Config Menu to show new active state
        var configItems = GenerateConfigItems();
        SetItems(configItems);
        
        // Update Appearance immediately to reflect new profile colors
        ApplyAppearance(); 
    }

    private void TogglePinPosition()
    {
        if (App.Config == null) return;

        var profile = App.Config.ActiveProfile;
        if (profile.PositionMode == RadiMenu.Models.PositionMode.Fixed)
        {
            profile.PositionMode = RadiMenu.Models.PositionMode.FollowMouse;
        }
        else
        {
            profile.PositionMode = RadiMenu.Models.PositionMode.Fixed;
            try 
            {
               // Capture current center position relative to screen
               Point centerPoint = this.PointToScreen(new Point(CenterX, CenterY));
               profile.FixedPosition = centerPoint;
            }
            catch { /* Ignore if not attached */ }
        }
        App.Config.SaveSettings();
        
        // Refresh Config Menu
        var configItems = GenerateConfigItems();
        SetItems(configItems);
    }

    private void OpenSettings()
    {
        // Hide menu first
         var window = Window.GetWindow(this) as MainWindow;
         window?.HideMenu();
         
         // Open Settings Window
         var settingsWin = new RadiMenu.Views.SettingsWindow();
         settingsWin.Show();
         
         // Exit config mode so next open is normal
         _isConfigMode = false;
    }

    public void RefreshMenu()
    {
        CloseSubmenu();
        LoadDefaultItems();
        RenderItems();
    }

    // --- CONTEXT MENU LOGIC ---

    private bool _isContextMenuOpen = false;

    private void ShowContextMenu(RadiMenu.Models.MenuItem item, int index, bool isSubmenu)
    {
        // Lock interaction immediately
        _isContextMenuOpen = true;

        var cm = new ContextMenu();
        // Remove Opened handler as we set it manually
        cm.Closed += (s, e) => {
            _isContextMenuOpen = false;
        };
        
        // Header (Label)
        var header = new System.Windows.Controls.MenuItem { Header = string.IsNullOrEmpty(item.Label) ? "Item" : item.Label, IsEnabled = false, FontWeight = FontWeights.Bold };
        cm.Items.Add(header);
        cm.Items.Add(new Separator());

        // 1. Insert Item
        var add = new System.Windows.Controls.MenuItem { Header = "Insertar Item Aquí" };
        add.Click += (s, e) => InsertItemAction(index, isSubmenu);
        cm.Items.Add(add);

        // 2. Change Icon
        var icon = new System.Windows.Controls.MenuItem { Header = "Cambiar Icono" };
        icon.Click += (s, e) => ChangeIconAction(item);
        cm.Items.Add(icon);

        // 3. Edit (Label/Path)
        var edit = new System.Windows.Controls.MenuItem { Header = "Editar..." };
        edit.Click += (s, e) => EditItemAction(item);
        cm.Items.Add(edit);

        // 4. Submenu Toggle
        var sub = new System.Windows.Controls.MenuItem { Header = item.HasSubItems ? "Eliminar Submenú" : "Convertir en Submenú" };
        sub.Click += (s, e) => ToggleSubmenuAction(item);
        cm.Items.Add(sub);

        cm.Items.Add(new Separator());

        // 6. Delete
        var del = new System.Windows.Controls.MenuItem { Header = "Eliminar", Foreground = System.Windows.Media.Brushes.Red };
        del.Click += (s, e) => DeleteItemAction(item, isSubmenu);
        cm.Items.Add(del);

        // Styling is now handled by App.xaml resources
        cm.IsOpen = true;
    }



    private void InsertItemAction(int index, bool isSubmenu)
    {
        // Add new item after current index
        var newItem = new RadiMenu.Models.MenuItem { Label = "Nuevo", Icon = "Add", AppPath = "" };
        
        if (isSubmenu)
        {
             if (_submenuItems == null) return;
             int target = index + 1;
             if (target > _submenuItems.Count) target = _submenuItems.Count; // Append
             
             _submenuItems.Insert(target, newItem);
             
             // Sync back to parent item
             if (_parentItemIndex >= 0 && _parentItemIndex < _items.Count)
             {
                 _items[_parentItemIndex].SubItems = _submenuItems;
             }

             RenderSubmenuRing();
        }
        else
        {
             if (App.Config?.ActiveProfile?.MenuItems == null) return;
             int target = index + 1;
             if (target > App.Config.ActiveProfile.MenuItems.Count) target = App.Config.ActiveProfile.MenuItems.Count;
             
             App.Config.ActiveProfile.MenuItems.Insert(target, newItem);
             RefreshMenu();
        }
        App.Config.SaveCurrentProfile();
    }




    private async void DeleteItemAction(RadiMenu.Models.MenuItem item, bool isSubmenu)
    {
        if (!await ShowConfirmationAsync("¿Está seguro de que desea eliminar este ítem?", "Eliminar"))
        {
            return;
        }

        if (isSubmenu)
        {
            if (_submenuItems != null)
            {
                _submenuItems.Remove(item);
                
                // Sync back to parent item
                if (_parentItemIndex >= 0 && _parentItemIndex < _items.Count)
                {
                    _items[_parentItemIndex].SubItems = _submenuItems;
                }

                if (_submenuItems.Count == 0) CloseSubmenu();
                else RenderSubmenuRing();
            }
        }
        else
        {
             if (App.Config?.ActiveProfile?.MenuItems != null)
             {
                 App.Config.ActiveProfile.MenuItems.Remove(item);
                 RefreshMenu();
             }
        }
        App.Config.SaveCurrentProfile();
    }

    private void ChangeIconAction(RadiMenu.Models.MenuItem item)
    {
        var parentWindow = Window.GetWindow(this);
        var picker = new RadiMenu.Views.IconPickerWindow { Owner = parentWindow };
        
        if (picker.ShowDialog() == true && picker.SelectedIconName != null)
        {
            item.Icon = picker.SelectedIconName; 
            
            // Clear AppPath/Command if needed/desired? 
            // The logic in Settings priorities Icon property if present. 
            // We'll keep existing AppPath just in case they switch back later or it's used for execution.
            
            if (_submenuItems != null && _submenuItems.Contains(item)) RenderSubmenuRing();
            else RefreshMenu();
            
            App.Config.SaveCurrentProfile();
        }
    }

    private async void ToggleSubmenuAction(RadiMenu.Models.MenuItem item)
    {
        if (item.HasSubItems)
        {
            if (await ShowConfirmationAsync("¿Eliminar todos los sub-ítems?", "Confirmar"))
            {
                item.SubItems = null;
            }
        }
        else
        {
            item.SubItems = new List<RadiMenu.Models.MenuItem>
            {
                new RadiMenu.Models.MenuItem { Label = "Sub 1", Icon = "Asterisk" },
                new RadiMenu.Models.MenuItem { Label = "Sub 2", Icon = "Asterisk" }
            };
        }
        RefreshMenu();
        App.Config.SaveCurrentProfile();
    }

    private void EditItemAction(RadiMenu.Models.MenuItem item)
    {
        var setWin = new RadiMenu.Views.SettingsWindow();
        setWin.Show();
    }

    // Helper class for FontIcon in ContextMenu
    private class FontIcon : TextBlock
    {
        public string Glyph { set { Text = value; } }
        public FontIcon()
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");
            FontSize = 14;
            VerticalAlignment = System.Windows.VerticalAlignment.Center;
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        }
    }
}
