using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadiMenu.Services;

public static class InputService
{
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf(typeof(INPUT));
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public System.IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { /* omitted for brevity */ }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT { /* omitted for brevity */ }

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // Virtual Key Codes
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12; // ALT
    private const ushort VK_LWIN = 0x5B;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }


    public static async Task SimulateKeyCombo(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return;

        var parts = combo.Split('+');
        var keysToRelease = new List<byte>();

        // 1. Press all keys
        foreach (var part in parts)
        {
            string cleanPart = part.Trim();
            ushort vk = ParseKey(cleanPart);
            
            if (vk != 0)
            {
                byte bVk = (byte)vk;
                keybd_event(bVk, 0, 0, UIntPtr.Zero); // Key Down
                keysToRelease.Add(bVk);
            }
        }

        // 2. Wait
        await Task.Delay(50); // Slightly larger delay for stability

        // 3. Release all keys (Reverse order)
        keysToRelease.Reverse();
        foreach (var bVk in keysToRelease)
        {
            keybd_event(bVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Key Up
        }
    }

    private static ushort ParseKey(string keyFunc)
    {
        keyFunc = keyFunc.ToUpper();
        switch (keyFunc)
        {
            case "CTRL": return VK_CONTROL;
            case "ALT": return VK_MENU;
            case "SHIFT": return VK_SHIFT;
            case "WIN": return VK_LWIN;
            
            // Letters / Numbers (A-Z, 0-9 use standard ASCII)
            default:
                if (keyFunc.Length == 1)
                {
                    char c = keyFunc[0];
                    if (char.IsLetterOrDigit(c)) return (ushort)c;
                }
                
                // Function keys
                if (keyFunc.StartsWith("F") && keyFunc.Length > 1 && int.TryParse(keyFunc.Substring(1), out int fNum))
                {
                    if (fNum >= 1 && fNum <= 24) return (ushort)(0x70 + (fNum - 1));
                }

                // Others
                if (keyFunc == "ESC" || keyFunc == "ESCAPE") return 0x1B;
                if (keyFunc == "SPACE") return 0x20;
                if (keyFunc == "ENTER" || keyFunc == "RETURN") return 0x0D;
                if (keyFunc == "TAB") return 0x09;
                if (keyFunc == "BACK" || keyFunc == "BACKSPACE") return 0x08;
                if (keyFunc == "DEL" || keyFunc == "DELETE") return 0x2E;

                try 
                {
                    // Fallback to WPF Key Converter for obscure keys, but cast to VirtualKey
                    if (Enum.TryParse<System.Windows.Input.Key>(keyFunc, true, out var wpfKey))
                    {
                         return (ushort)System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey);
                    }
                }
                catch { }

                return 0;
        }
    }
    public static System.Windows.Point GetMousePosition()
    {
        Point lpPoint;
        GetCursorPos(out lpPoint);
        return new System.Windows.Point(lpPoint.X, lpPoint.Y);
    }
}
