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
    
    // Drag-to-reorder state
    private bool _isDragging = false;
    private int _dragSourceIndex = -1;
    private long _mouseDownTime = 0;
    private bool _mouseIsDown = false;
    private const long DragHoldThreshold = 300; // ms before drag activates
    private bool _isDraggingSubmenuItem = false; // true if dragging in submenu
    private int _submenuDragSourceIndex = -1;
    
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
        
        // Reset drag state
        _isDragging = false;
        _dragSourceIndex = -1;
        _mouseIsDown = false;
        _isDraggingSubmenuItem = false;
        _submenuDragSourceIndex = -1;
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
        
        // Sistema de coordenadas:
        // - rotatedAngle en MouseMove: 0° = arriba, aumenta en sentido horario
        // - ángulos para dibujar: -90° = arriba en sistema matemático
        
        double parentAngle = 360.0 / _items.Count;
        
        // El ítem padre ocupa desde (_parentItemIndex * parentAngle) hasta ((_parentItemIndex + 1) * parentAngle)
        // en el sistema visual (0° = arriba). Para dibujar, restamos 90°.
        double parentStartVisual = _parentItemIndex * parentAngle; // Inicio en sistema visual
        double parentStartDraw = parentStartVisual - 90; // Convertir a sistema de dibujo
        
        double subAngleStep = parentAngle / _submenuItems.Count;
        
        double innerR = CenterCircle.Width / 2;
        double thickness = App.Config?.ActiveProfile?.Appearance?.RingThickness ?? 85;
        double outerRingStart = innerR + thickness + 5;
        double outerRingThickness = thickness; // Same as main ring
        double submenuIconRadius = outerRingStart + (outerRingThickness / 2);
        
        // First render background arc (so it's behind icons)
        RenderSubmenuArc(parentStartDraw, parentAngle, outerRingStart, outerRingThickness);
        
        // Then render icons on top
        for (int i = 0; i < _submenuItems.Count; i++)
        {
            var item = _submenuItems[i];
            double itemAngle = parentStartDraw + (i * subAngleStep) + (subAngleStep / 2);
            
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
        // Create a path for the submenu arc background
        // IMPORTANT: IsHitTestVisible = false so it doesn't block mouse events
        var arcPath = new Path
        {
            Fill = MainRing.Fill,
            Tag = "submenu",
            IsHitTestVisible = false
        };
        
        double outerRadius = radius + thickness;
        double innerRadius = radius;
        
        // Convert to radians
        double startRad = startAngle * Math.PI / 180;
        double endRad = (startAngle + sweepAngle) * Math.PI / 180;
        
        // Calculate points
        Point outerStart = new Point(CenterX + outerRadius * Math.Cos(startRad), CenterY + outerRadius * Math.Sin(startRad));
        Point outerEnd = new Point(CenterX + outerRadius * Math.Cos(endRad), CenterY + outerRadius * Math.Sin(endRad));
        Point innerEnd = new Point(CenterX + innerRadius * Math.Cos(endRad), CenterY + innerRadius * Math.Sin(endRad));
        Point innerStart = new Point(CenterX + innerRadius * Math.Cos(startRad), CenterY + innerRadius * Math.Sin(startRad));
        
        bool isLargeArc = sweepAngle > 180;
        
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = outerStart };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));
        figure.IsClosed = true;
        geometry.Figures.Add(figure);
        
        arcPath.Data = geometry;
        
        // Insert at beginning so it's behind items but the arc is added first before icons
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
        
        // --- DRAG DETECTION ---
        if (_mouseIsDown && _dragSourceIndex >= 0 && !_isDragging)
        {
            long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            if (currentTime - _mouseDownTime >= DragHoldThreshold)
            {
                // Activate drag mode
                _isDragging = true;
                StartDragFeedback(_dragSourceIndex);
            }
        }
        
        // If actively dragging, handle reorder logic
        if (_isDragging && distance >= _minHitRadius && distance <= _maxHitRadius)
        {
            // Calculate which position the drag is over
            double shiftedAngle = rotatedAngle + (step / 2);
            while (shiftedAngle >= 360) shiftedAngle -= 360;
            int targetIndex = (int)(shiftedAngle / step);
            
            if (targetIndex >= 0 && targetIndex < count && targetIndex != _dragSourceIndex)
            {
                // Swap items
                SwapItems(_dragSourceIndex, targetIndex);
                _dragSourceIndex = targetIndex;
            }
            return; // Don't process normal hover while dragging
        }
        // --- END DRAG ---


        // 2. Handle Submenu Priority
        if (_submenuItems != null)
        {
            if (distance > _maxHitRadius + 150 || distance < _minHitRadius - 10)
            {
                CloseSubmenu();
            }
            else if (distance > _maxHitRadius)
            {
                _lastMousePos = mousePos;

                // El ítem padre ocupa [_parentItemIndex * step, (_parentItemIndex + 1) * step)
                // en rotatedAngle (sistema visual). Calculamos ángulo relativo dentro de esa porción.
                double parentStartVisual = _parentItemIndex * step;
                double relativeAngle = rotatedAngle - parentStartVisual;
                
                // Normalizar a [0, 360)
                while (relativeAngle < 0) relativeAngle += 360;
                while (relativeAngle >= 360) relativeAngle -= 360;

                // Si está dentro del rango [0, step), está sobre el submenú
                if (relativeAngle < step)
                {
                    double subStep = step / _submenuItems.Count;
                    int subIndex = (int)(relativeAngle / subStep);
                    if (subIndex < 0) subIndex = 0;
                    if (subIndex >= _submenuItems.Count) subIndex = _submenuItems.Count - 1;
                    
                    if (subIndex != _hoveredSubmenuIndex)
                    {
                        SelectSubmenuItem(subIndex);
                    }
                }
                else
                {
                    if (_hoveredSubmenuIndex != -1) SelectSubmenuItem(-1);
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
            double parentStep = 360.0 / _items.Count;
            double subStep = parentStep / _submenuItems.Count;
            
            double outerRingStart = innerR + ringThickness + 5;
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
            double parentStartVisual = _parentItemIndex * parentStep;
            double targetAngle = parentStartVisual + (index * subStep) + (subStep / 2);
            
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
    
    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
         if (e.LeftButton != MouseButtonState.Pressed) return;
         
         var mousePos = e.GetPosition(RootGrid);
         double dx = mousePos.X - CenterX;
         double dy = mousePos.Y - CenterY;
         double distance = Math.Sqrt(dx * dx + dy * dy);
         
         // Record mouse down state for potential drag
         _mouseIsDown = true;
         _mouseDownTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
         
         // Check if in submenu ring first
         if (_submenuItems != null && _hoveredSubmenuIndex >= 0 && distance > _maxHitRadius)
         {
             _submenuDragSourceIndex = _hoveredSubmenuIndex;
             _dragSourceIndex = -1; // Not dragging main menu
             RootGrid.CaptureMouse();
             return;
         }
         
         // If in main ring, capture mouse for drag detection
         if (distance >= _minHitRadius && distance <= _maxHitRadius && _selectedIndex >= 0)
         {
             _dragSourceIndex = _selectedIndex;
             _submenuDragSourceIndex = -1; // Not dragging submenu
             RootGrid.CaptureMouse();
         }
    }
    
    private void RootGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
         RootGrid.ReleaseMouseCapture();
         
         var mousePos = e.GetPosition(RootGrid);
         double dx = mousePos.X - CenterX;
         double dy = mousePos.Y - CenterY;
         double distance = Math.Sqrt(dx * dx + dy * dy);
         
         // DEBUG: Log all state at MouseUp
         System.Diagnostics.Debug.WriteLine($"[MouseUp] distance={distance:F1}, _maxHitRadius={_maxHitRadius:F1}, submenu={_submenuItems != null}, inSubmenuZone={(distance > _maxHitRadius)}");
         
         // If we were dragging main menu, finish and save
         if (_isDragging)
         {
             _mouseIsDown = false;
             FinishDrag();
             return;
         }
         
         // If we were dragging submenu, finish and save
         if (_isDraggingSubmenuItem)
         {
             _mouseIsDown = false;
             FinishSubmenuDrag();
             return;
         }
         
         // Reset drag state
         _mouseIsDown = false;
         _dragSourceIndex = -1;
         _submenuDragSourceIndex = -1;
         
         // 1. Submenu Ring Click - calculate the clicked submenu item at click time
         //    instead of relying on stored _hoveredSubmenuIndex which may be stale
         if (_submenuItems != null && _submenuItems.Count > 0 && distance > _maxHitRadius)
         {
             // Calculate angle to determine which submenu item was clicked
             double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
             if (angle < 0) angle += 360;
             double rotatedAngle = angle + 90;
             if (rotatedAngle >= 360) rotatedAngle -= 360;
             
             int count = _items.Count;
             double step = 360.0 / count;
             double parentStartVisual = _parentItemIndex * step;
             double relativeAngle = rotatedAngle - parentStartVisual;
             
             // Normalize to [0, 360)
             while (relativeAngle < 0) relativeAngle += 360;
             while (relativeAngle >= 360) relativeAngle -= 360;
             
             // Check if within submenu angular range
             if (relativeAngle < step)
             {
                 double subStep = step / _submenuItems.Count;
                 int subIndex = (int)(relativeAngle / subStep);
                 if (subIndex < 0) subIndex = 0;
                 if (subIndex >= _submenuItems.Count) subIndex = _submenuItems.Count - 1;
                 
                 System.Diagnostics.Debug.WriteLine($"[MouseUp] Submenu click detected! subIndex={subIndex}, distance={distance}, _maxHitRadius={_maxHitRadius}");
                 
                 var item = _submenuItems[subIndex];
                 if (item.IsSubmenuOnly)
                 {
                     OpenSubmenu(item);
                 }
                 else
                 {
                     ItemSelected?.Invoke(this, item);
                 }
                 return;
             }
         }

         // 2. Main Ring Click
         if (distance >= _minHitRadius && distance <= _maxHitRadius)
         {
             if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
             {
                 var item = _items[_selectedIndex];
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
         
         _dragSourceIndex = -1;
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
    
    private void FinishDrag()
    {
        _isDragging = false;
        _dragSourceIndex = -1;
        
        // Clear all animations and reset transforms
        foreach (var control in _menuItemControls)
        {
            // Stop any running animations
            control.BeginAnimation(Canvas.LeftProperty, null);
            control.BeginAnimation(Canvas.TopProperty, null);
            control.RenderTransform = null;
            control.Opacity = 1.0;
        }
        
        // Re-render to fix any position issues
        RenderItems();
        
        // Save the new order to config
        if (App.Config?.ActiveProfile != null)
        {
            App.Config.ActiveProfile.MenuItems = _items;
            App.Config.SaveSettings();
        }
        
        // Update label and selection
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            SelectionLabel.Text = _items[_selectedIndex].Label;
            SelectItem(_selectedIndex, false);
        }
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
        
        targetControl.RenderTransform = null;
        targetControl.Opacity = 1.0;
        
        _hoveredSubmenuIndex = targetIndex;
    }
    
    private void FinishSubmenuDrag()
    {
        _isDraggingSubmenuItem = false;
        _submenuDragSourceIndex = -1;
        
        // Reset visual feedback
        foreach (var control in _submenuItemControls)
        {
            control.BeginAnimation(Canvas.LeftProperty, null);
            control.BeginAnimation(Canvas.TopProperty, null);
            control.RenderTransform = null;
            control.Opacity = 1.0;
        }
        
        // Update the parent item's SubItems with the new order
        if (_parentItemIndex >= 0 && _parentItemIndex < _items.Count && _submenuItems != null)
        {
            _items[_parentItemIndex].SubItems = _submenuItems;
            
            // Save to config
            if (App.Config?.ActiveProfile != null)
            {
                App.Config.ActiveProfile.MenuItems = _items;
                App.Config.SaveSettings();
            }
        }
        
        // Re-render submenu to fix positions
        RenderSubmenuRing();
        
        // Update label
        if (_hoveredSubmenuIndex >= 0 && _hoveredSubmenuIndex < _submenuItems?.Count)
        {
            SelectionLabel.Text = _submenuItems[_hoveredSubmenuIndex].Label;
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
        return distance <= _maxHitRadius;
    }

    private void CenterCircle_MouseEnter(object sender, MouseEventArgs e) { }
    private void CenterCircle_MouseLeave(object sender, MouseEventArgs e) { }

    private bool _isConfigMode = false;

    private void CenterCircle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // If we're in a submenu, go back to parent menu
        if (CanGoBack)
        {
            GoBack();
            return;
        }
        
        // Otherwise toggle config mode
        ToggleConfigMode();
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
}
