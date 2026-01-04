using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace RadiMenu.Services;

public static class IconGenerator
{
    public static Icon GenerateAppIcon()
    {
        // Canvas size
        int size = 64; 
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Define colors
        var brush = new SolidBrush(Color.White); // Or accent color? User SVG has "currentColor", usually implies White/Black depending on themes. Let's use White for Tray (Dark mode usually).
        // Actually, for Windows Tray, usually Black or White depending on theme?
        // Let's use a standard color, maybe the App Accent or strict Black/White?
        // User's SVG has default fill="currentColor".
        // Let's default to White as it's common for tray icons on dark taskbars.
        // We can check system theme later if needed. Use White for now.
        
        // --- SCALE ---
        // SVG is 24x24. We are drawing on 64x64. Scale factor = 64/24 = 2.66
        g.ScaleTransform(size / 24f, size / 24f);

        // --- DRAW RING ---
        // M12 2 ... (Outer R=10)
        // M12 6 ... (Inner R=6)
        // Center (12, 12).
        
        var path = new GraphicsPath();
        path.FillMode = FillMode.Alternate; // EvenOdd
        
        // Outer Circle (Center 12,12, Radius 10) -> Bounds (2, 2, 20, 20)
        path.AddEllipse(2, 2, 20, 20);
        
        // Inner Circle (Center 12,12, Radius 6) -> Bounds (6, 6, 12, 12)
        path.AddEllipse(6, 6, 12, 12);
        
        // We need to exclude the RECTANGLES.
        // GraphicsPath doesn't support "DestinationOut" composition directly in expected way easily for single path?
        // Actually, we can just use Regions or draw the Ring then "Erase" with the rects.
        
        // Better approach: Create Region from Ring, prevent drawing in Rect areas.
        
        var ringRegion = new Region(path);
        
        // Create Rect regions to exclude
        // 1. Vertical: x=11, y=-1, w=2, h=26
        var r1 = new RectangleF(11, -1, 2, 26);
        ringRegion.Exclude(r1);
        
        // 2. Horizontal: x=-1, y=11, w=26, h=2
        var r2 = new RectangleF(-1, 11, 26, 2);
        ringRegion.Exclude(r2);
        
        // 3. Rotated 45
        // To exclude rotated rects, we need a path
        var p3 = new GraphicsPath();
        p3.AddRectangle(new RectangleF(11, -1, 2, 26));
        // Rotate around 12,12
        var matrix3 = new Matrix();
        matrix3.RotateAt(45, new PointF(12, 12));
        p3.Transform(matrix3);
        ringRegion.Exclude(p3);
        
        // 4. Rotated -45
        var p4 = new GraphicsPath();
        p4.AddRectangle(new RectangleF(11, -1, 2, 26));
        var matrix4 = new Matrix();
        matrix4.RotateAt(-45, new PointF(12, 12));
        p4.Transform(matrix4);
        ringRegion.Exclude(p4);
        
        // Draw the resulting region
        g.FillRegion(brush, ringRegion);
        
        // Create Icon from Bitmap
        // GetHicon creates an unmanaged handle, we must be careful to destroy it?
        // Icon.FromHandle creates a managed wrapper using the handle.
        // We usually need to destroy the generic HICON?
        // System.Drawing.Icon.FromHandle creates a copy? No, it uses it.
        // Recommended approach for temp icons:
        
        return Icon.FromHandle(bitmap.GetHicon());
    }
    
    public static void EnsureIconFile(string path)
    {
        try
        {
            if (File.Exists(path)) return;
            
            using var icon = GenerateAppIcon();
            using var fs = new FileStream(path, FileMode.Create);
            icon.Save(fs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconGenerator] Error saving icon: {ex.Message}");
        }
    }
}
