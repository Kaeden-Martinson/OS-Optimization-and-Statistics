# Optiframe: name, logo and move-and-pin overlay — design

Date: 2026-10-24 · Ships with: 2.1.0 (with the Instrument redesign) · Status: approved in brainstorming, awaiting spec review

## Goal

1. Rename the app from "PC Spec Overlay" to **Optiframe** everywhere a person sees it.
2. Give it a logo, the **Viewfinder** mark: four corner brackets (the frame) around a frame-time line with one spike.
3. Let the user **drag the overlay anywhere and pin it there**, with the four corners kept as one-click presets.

Mockups picked in the visual companion: `logo.html` option A, `move-pin.html` (copied next to this spec in
`2026-10-24-optiframe-mockups/`).

## 1. Name

**Changes (visible):** window titles ("Optiframe", "Optiframe Setup", overlay window "Optiframe overlay"); the tab
bar ("Optiframe"); the Panel overlay header ("OPTIFRAME"); the tray tooltip and default notification title; the
installer's headings and messages; the Start-menu shortcut ("Optiframe.lnk"); the Apps & Features entry
(DisplayName and Publisher); the diagnostic report heading; the startup task's description; the file
description and product name Task Manager shows (`Product`, `AssemblyTitle`); the release script's setup file name
(`Optiframe-Setup-<version>.exe`), its messages and release notes heading; the updater's download file name;
README.

**Unchanged (internal):** the program file name `PCSpecOverlay.exe`, assembly name, namespaces, install folder
`%LocalAppData%\Programs\PCSpecOverlay`, data folder `%AppData%\PCSpecOverlay`, startup task `\PCSpecOverlay`,
single-instance mutex, events, ETW session, uninstall registry key, settings keys. Settings, history and
"Start with Windows" carry over with no migration. The GitHub repository name is unchanged (it can be renamed on
GitHub later; GitHub redirects).

**Upgrade cleanup:** installing over an old copy deletes the old "PC Spec Overlay.lnk" Start-menu shortcut;
uninstall removes both the new and the old shortcut if present.

The setup program is still recognized by "Setup" in its file name, and the updater still picks the release asset
whose name contains "Setup" and ends in ".exe", so 2.0.13 can update to 2.1.0 unchanged.

## 2. Logo

**Geometry** (24-unit grid, strokes only, round caps and joins):
brackets `M3,8 V3 H8 · M16,3 H21 V8 · M21,16 V21 H16 · M8,21 H3 V16`, trace
`M6,14 H9.5 L11.5,8.5 L13.5,15.5 L15,12 H18`.

- **In the app:** `Icon.Mark` in `Themes/Icons.xaml` becomes this geometry, so the tab bar shows it in the text
  color. The installer window shows it next to its heading.
- **App icon (`Optiframe.ico`):** the mark in `#E9E9E6` on a `#0B0B0C` square tile, sizes 16, 20, 24, 32, 40,
  48, 64, 256 (PNG-compressed entries). Stroke 2 units (2.4 at 16 px). Set as the exe's `ApplicationIcon` and as the
  icon of the control-center and installer windows.
- **Tray icon:** the mark without a tile, drawn at the system small-icon size, in `#141414` when the Windows
  taskbar is light (`SystemUsesLightTheme` = 1) and `#E9E9E6` otherwise; redrawn when Windows' theme changes.
  This replaces the last NVIDIA green.
- One GDI+ painter (`LogoPainter`) draws the tray icon and writes the .ico from the same point list; a test
  keeps the committed .ico identical to the painter's output.

## 3. Move and pin

**Settings:** `Position` gains the value `Custom` (alongside `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft`).
New `CustomX`, `CustomY` (0–1): where the overlay's panel sits within the free space of the chosen display's work
area (0 = left/top edge, 1 = right/bottom edge), so the spot survives resolution, scaling and size changes and the
panel always stays fully on screen. Defaults 1 and 0 (top right). `Monitor` (existing) names the display; if it
is gone, the main display is used. Invalid or non-finite values are clamped or reset on load.
`ClickThrough` is removed: pinned always means click-through; old settings files that contain it still load
(the field is ignored).

**Unlocking:** "Move overlay" in the tray menu and on the Overlay settings page. It shows the overlay if hidden
and enters move mode: a dashed outline around the panel, a handle bar above it ("Drag to move" with a **Pin**
button), and the overlay stops passing clicks through. Move mode is not saved; a restart comes back pinned.

**Dragging:** press anywhere on the overlay (except Pin) and drag. On release the overlay stays exactly there (no
snapping); its display is the one containing the panel's center; the spot is stored as `Custom` + fractions; the
panel is kept fully inside that display's work area.

**Pinning:** Pin hides the outline and handle, restores click-through, saves, and shows a short tray notice
("Overlay pinned"). While moving, the handle sits above the panel (below it when there is no room above, so Pin is always on screen); the panel itself stays where it will be pinned.

**Presets:** the Overlay page's position choice lists Top right, Top left, Bottom right, Bottom left and
"Custom (pinned spot)". Choosing a corner moves the overlay there. The control center's choice updates when the
overlay is dragged.

**Replaced:** the old "Click-through" switch and its drag-to-nearest-corner behavior.

## Testing

Automated: placement math (corners, custom fractions, clamping, too-small work areas, round trip
spot → fractions → spot); settings validation (Custom, fractions, old files with `ClickThrough`); logo painter
(mark pixels drawn, transparent and tiled backgrounds) and the .ico (every size present, identical to the
painter); taskbar-theme resolution; source checks that no visible "PC Spec Overlay" remains outside the legacy
constant, that the windows carry the icon, and that the Overlay page offers Custom and Move overlay.

On the PC: tray shows the new icon in the taskbar's theme; app/installer/Task Manager show Optiframe and the icon;
move, drag across the screen, pin, restart and confirm the spot; corner presets; an update from an old install
leaves only the new Start-menu shortcut.

## Out of scope

Renaming internal identifiers or the GitHub repository; snapping or guides while dragging; a hotkey for move mode;
per-game positions.
