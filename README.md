# RadiMenu

A Windows Radial Menu (concept similar to Surface Dial) replicated in WPF.

## Getting Started

### Prerequisites

*   .NET 8.0 SDK
*   Windows OS (WPF dependency)

### How to Clone on a New PC

If you have GitHub CLI (`gh`) installed:
```powershell
gh repo clone xmmanuellx/radimenu
cd radimenu
dotnet run
```

Or using Git directly:
```powershell
git clone https://github.com/xmmanuellx/radimenu.git
cd radimenu
dotnet run
```

### Controls

*   **Open Menu**: Press `Ctrl + Space` (or your configured hotkey).
*   **Navigation**: Move mouse to select items.
*   **Submenus**: Click a submenu item to open it. Click outside or Escape to close.
*   **Drag & Drop**: Long press (hold ~300ms) on an item to drag and reorder it.
