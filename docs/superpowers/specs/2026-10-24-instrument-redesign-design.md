# Instrument redesign — design

Date: 2026-10-24 · Target release: 2.1.0 · Status: approved in brainstorming, awaiting spec review

## Goal

Redesign every visible part of PC Spec Overlay (control center, in-game overlay, tray menu, installer) in one
visual language, "Instrument", with light and dark themes switchable while the app runs. The look should read as
a precise measuring tool: monochrome, ruled lines, monospaced numbers, and color only when something needs
attention. No emojis anywhere; every icon is a vector path.

This is a redesign of appearance and arrangement. Every existing setting, check, safety rule, shortcut and stored
file keeps working. New behavior is limited to the items marked **New** below.

## Choices made

Mockups were reviewed in the brainstorming visual companion; the picked files are copied next to this spec in
`2026-10-24-instrument-redesign-mockups/` (HTML fragments; they render inside the companion's frame template).

| Screen | Choice | Mockup |
|---|---|---|
| Overall direction | B, Instrument | `visual-direction.html` |
| In-game overlay | B, Strip by default; A, Panel available | `overlay.html` |
| Optimizer | A, Ledger | `optimizer.html` |
| Gaming | B, Pipeline | `gaming.html` |
| Overlay settings | A, Preview first | `overlay-settings.html` |
| Temperatures & alerts | B, Scales | `alerts.html` |
| Settings | C, Status strip | `settings.html` |
| Diagnostics | B, Health grid | `diagnostics.html` |
| Tray menu | A, Plain menu | `tray-installer.html` |
| Installer | A, One card | `tray-installer.html` |

## 1. Visual system

### Color tokens

Same keys in both themes. These replace today's NVIDIA-green palette; there is no brand color.

| Key | Dark | Light | Use |
|---|---|---|---|
| `Bg` | `#0B0B0C` | `#F3F2EE` | Window background |
| `Panel` | `#111113` | `#FBFAF7` | Raised areas, selected rows, inputs |
| `Line` | `#262629` | `#D9D7D0` | Ruled grid, section borders |
| `Line2` | `#1A1A1C` | `#E6E4DD` | Row dividers |
| `Text` | `#E9E9E6` | `#141414` | Primary text, primary button fill |
| `Muted` | `#87878A` | `#66665F` | Secondary text, labels |
| `Faint` | `#57575B` | `#9C9A92` | Axis ticks and dividers only, never readable text |
| `Signal` | `#E8A33D` | `#B4730F`* | Warning state only |
| `Hot` | `#E5534B` | `#B8322B` | Hot state only (the only red in the app) |
| `Good` | `#9BCB58` | `#4E7A12`* | Status lights and check marks only |

\* Light `Signal` and `Good` will be darkened during implementation until small text meets a 4.5:1 contrast
ratio on `Bg` and `Panel` (first estimates: about `#9A5F00` and `#47700F`). All body text pairs must meet 4.5:1.

Today's colors hard-coded in C# (about 15 places in `ControlCenterWindow.xaml.cs` and `OverlayWindow.xaml.cs`:
temperature colors, log-level colors, chart lines, tinted pill backgrounds) move into the token set, including
translucent variants, so they recolor with the theme.

### Type

- Text: Segoe UI (Segoe UI Variable Display for page titles).
- Every number: Cascadia Mono, falling back to Consolas. Tabular digits so values don't jitter.
- Scale: 10.5 px uppercase labels (letter-spaced), 12–12.5 px body, 16 px page titles, 26–30 px headline readings.

### Shape

Square corners, 1 px ruled lines, no drop shadows. Switches are square (track and thumb). Selected tab: 2 px
underline in `Text`. Selected list row: `Panel` fill with a 2 px left edge in `Text`. Primary button: `Text` fill
with `Bg` lettering; other buttons: 1 px `Line` border.

### Icons

One vector icon set (24-unit grid, 1.6 stroke, round caps and joins) drawn in the current text color, defined
once as geometry resources: app mark, overview, optimizer, gaming, overlay, temperature, settings, diagnostics,
sun, moon, monitor, check, warning, info, undo, pause, plus, close, chevron, refresh, copy, file, folder, pointer,
power, grip, window. Paths match the mockups. No icon fonts, no image files, no emojis.

## 2. Theme switching

- **New** setting `Appearance`: `Light`, `Dark`, `MatchWindows` (default `MatchWindows`).
  Match Windows reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`
  (1 = light). If missing or unreadable, dark. While Match Windows is on, the app listens for Windows
  preference changes and switches without a restart.
- Sun/moon button at the right of the tab bar: flips immediately and saves `Light` or `Dark`.
- Two resource dictionaries with identical keys (dark, light). Brushes are referenced with `DynamicResource`
  in XAML and `SetResourceReference` in code, so swapping the dictionary recolors everything live.
- The window title bar follows the theme (today `DarkTitleBar` forces dark; it becomes `TitleBar.Apply(window, dark)`).
- The tray menu gets a custom `ToolStripRenderer` using the same tokens.
- The installer window follows the theme.
- **New** overlay setting `OverlayTheme`: `SameAsApp`, `AlwaysDark` (default), `AlwaysLight`. Light overlays read
  poorly over bright games, so the overlay stays dark unless chosen otherwise.
- If a theme dictionary fails to load, fall back to dark and log the reason.

## 3. Screens

### Window shell

The left sidebar is replaced by a 38 px tab bar: app mark and "PC SPEC", then seven tabs (Overview, Optimizer,
Gaming, Overlay, Alerts, Settings, Diagnostics), then the version and the sun/moon button at the right. Minimum
window width about 840 px.

### Overview

Four readings in a ruled grid (Frame rate with source, GPU, CPU, Memory), each with a 10-segment bar. Below: the
frame-rate chart drawn as a line on a grid with dips marked, and an **New** event panel listing recent things
the app did and why (fed from the existing in-memory log, `Log.Recent()`, 300 entries; nothing new is stored).
A status line at the bottom: monitoring, power source, overlay visibility and shortcut.

### In-game overlay

- **Strip** (today's Compact, renamed): one ruled line — FPS, frame time, GPU temperature and load, CPU
  temperature and load. **New default for fresh installs.**
- **Panel** (today's Expanded, renamed): header (PC SPEC, FPS source), large FPS, frame time and 1% low,
  **New** live frame-time trace (rolling buffer of recent frame times, fixed size), then GPU, CPU and memory rows.
- Stored values stay `Compact`/`Expanded` so existing settings files load unchanged; only the display names change.
- Warning values in `Signal`, hot values in `Hot`; otherwise monochrome.
- The user's PC currently has `Expanded` saved. The app won't overwrite a saved choice; after 2.1.0 is installed
  the layout is switched to Strip through the setting itself, because the user picked Strip here.

### Optimizer — Ledger

Page header with "Apply N recommendations". One table: status mark, Setting, Now, Recommended, Effect and
drawback, action button. Rows needing action get a 2 px `Signal` left edge. Below, "What's using your PC" as a
second ruled table with the existing "Lower during games" and End controls. No behavior change.

### Gaming — Pipeline

Status bar under the tabs when gaming mode is on ("Gaming mode is on for Starve.io…") with Pause (existing
stop/resume). Four ruled steps: 01 Detect (auto switch, "after N s"), 02 Power (Best performance switch),
03 Boost (Game Boost switch), 04 Restore ("after N s"). Then the games table (Game, Match by, Overlay
Strip/Panel/Default, Optimize, remove), "Recently seen" with Add, and the fullscreen-detection and
always-boost options. No behavior change beyond the layout.

### Overlay settings — Preview first

Left: a 16:9 preview with the overlay drawn live from current settings; **New** click a dashed corner to move it
there (replaces the corner dropdown; the display picker stays). Under it two cards: Strip and Panel. Right
column: Show the overlay, Opacity, Text size, Show (metric checkboxes), Click-through, Refresh, GPU drawing,
and the new Overlay theme.

### Temperatures & alerts — Scales

**New** control: for GPU and CPU, a 30–100 °C scale with the warning band (`Signal`) and hot band (`Hot`)
shaded, two draggable handles, a line for the current reading and labeled values. Constraints: Warning < Hot,
both within 30–100 °C, 1 °C steps, keyboard arrows adjust the focused handle. Sensor source noted beside each
scale. Notification settings below as ledger rows.

### Settings — Status strip

Three ruled cells: Version (latest/checked time, Check now, Download and install when available), Startup (Start
with Windows switch), Appearance (Light / Dark / Match Windows segmented control). Below, two columns: Shortcut
and updates; Power and history (GPU sleep, keep history, summary retention, recent sessions). Restore defaults
at the bottom.

### Diagnostics — Health grid

Header with Copy report, Open log, Data folder, Undo every change. An 8-cell status grid (Frame rate,
Permission, NVIDIA, GPU power state, CPU temperature, Startup, Updates, Drawing), each with a check or warning
mark and a one-line detail. Below, a **New** change journal: each change currently in effect, when, owner
(Optimizer or Gaming), and an Undo button. Undo releases that change for all its owners through the existing
`ChangeJournal.Release` path, so the existing rule holds: the value is restored only if it still matches what
the app applied, otherwise it reports "changed elsewhere" and leaves the user's value. Past changes are not
listed (no history is stored).

### Tray menu — Plain

Same items as today (Open PC Spec, show/hide overlay with shortcut, click-through, pause gaming mode, start
with Windows, exit), drawn by the custom renderer in the active theme with vector icons and right-aligned
shortcuts.

### Installer — One card

Same content and flow as today (title, explanation, Start with Windows switch, Cancel and Install/Update), in
the Instrument style, following the theme.

## 4. Build order

Each stage leaves the app working and tested before the next starts.

1. Foundation: token dictionaries, `DynamicResource` conversion of every existing view, code-side colors
   moved to tokens, Appearance setting, title bar, icon geometry set. Light/dark works on the current layouts.
2. Overlay: Strip/Panel restyle, frame-time trace, overlay theme setting.
3. Shell and Overview: tab bar, Overview page.
4. Pages: Optimizer, Gaming, Overlay settings, Alerts (scale control), Settings, Diagnostics (journal).
5. Tray renderer and installer.
6. Release 2.1.0 with `build-release.ps1`, published to GitHub; the installed 2.0.13 offers it as an update.

## 5. Testing

Automated (existing 99 tests keep passing):

- Theme resolution: Light, Dark, Match Windows with registry 0, 1, missing, and wrong type → dark fallback.
- Settings compatibility: older `settings.json` files load; `Compact`/`Expanded` and missing `Appearance` /
  `OverlayTheme` values resolve to defaults; invalid values are replaced.
- Frame-time buffer: bounded size, oldest samples dropped, empty and single-sample cases.
- Scale mapping: position ↔ °C round-trips, clamping to 30–100, Warning < Hot enforced on drag and keyboard.
- Per-change Undo: restores when unchanged; returns ChangedElsewhere and keeps the user's value when changed.

On the real PC before each stage is called done:

- Screenshots of every page in light and dark, compared with the picked mockups.
- Contrast check of every token pair used for text (4.5:1 minimum).
- Screen-reader names present for every control (UI Automation, as checked before).
- Overlay CPU use measured before and after; no increase.
- With Match Windows on, flipping Windows between light and dark switches the running app.

## Out of scope

- Storing a history of past changes or alerts.
- Reordering overlay readings (the Builder option).
- A status flyout from the tray.
- Code signing (decided against).
- Any change to what the optimizer, gaming mode or alerts actually do.
