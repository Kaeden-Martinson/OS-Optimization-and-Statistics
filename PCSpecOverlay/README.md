# PC Spec Overlay

A lightweight, click-through performance overlay for Windows, in the style of NVIDIA's in-game overlay.

**Shows:** FPS · frame time · 1% low FPS · GPU load / temp / clock / power · VRAM · CPU load / temp · RAM

## Use

| Action | How |
|---|---|
| Open the Optimizer | Tray icon → **Optimize…** (or launch the exe again while it's running) |
| Game Boost on/off | Tray icon → Game Boost |
| Toggle overlay | **Alt+Shift+O** (or left-click the tray icon) |
| Move to another corner | Tray icon → Position |
| Start automatically at logon | Tray icon → Start with Windows |
| Change hotkey, opacity, size | Tray icon → Edit settings… |
| Quit | Tray icon → Exit |

Settings live in `%AppData%\PCSpecOverlay\settings.json`:

```json
{
  "Hotkey": "Alt+Shift+O",
  "Position": "TopRight",
  "Visible": true,
  "Opacity": 1.0,
  "Scale": 1.0
}
```

Restart the overlay after editing the file.

## Slowdown tags and alerts

When the hardware slows itself down, a tag appears under the stats:

| Tag | Meaning |
|---|---|
| **GPU · TOO HOT, SLOWING DOWN** (red) | NVIDIA reports a thermal slowdown: the GPU cut its clock to cool off. |
| **CPU · TOO HOT, SLOWING DOWN** (red) | Windows reports the CPU capped below full speed while it's 90 °C or hotter. |
| **GPU · POWER LIMIT** / **CPU · POWER LIMIT** (amber) | Running at its power cap. Normal at full load; plug in or use NitroSense Turbo for a higher cap. |

If a red one lasts 10 seconds or more you get a Windows notification (at most once every 10 minutes per
part; logged to `%AppData%\PCSpecOverlay\overlay.log`). Turn it off with tray → Overheating alerts.
The CPU row also shows the current CPU speed in GHz.

## Optimizer

No software can make hardware faster; the Optimizer removes the things that hold it back.
Every change remembers the original value and has an **Undo** button (originals are kept in
`settings.json`, so Undo works after a restart).

**System checks**

| Check | What Fix does |
|---|---|
| Power source | Info only. Laptops cap GPU power on battery. |
| Power mode | Sets Windows power mode to *Best performance* (higher, faster CPU boost). |
| Acer NitroSense / PredatorSense | Opens the app. Its Performance/Turbo mode and fan settings set the laptop's power limits, which matter more than anything else here. |
| Edge / Chrome graphics chip | Sets the browser to always use the NVIDIA GPU (Windows › Graphics settings). A **Restart Edge** banner then appears under Fix all: it closes the browser the way Windows does at shutdown (saving every window and tab) and reopens it with them restored. |
| Game Mode | Turns Windows Game Mode on. |
| Background recording | Turns off Xbox Game Bar's always-on "record what happened". |

**Game Boost.** While a game is in front and rendering (30+ FPS), it gets *Above normal* CPU priority,
and apps you switch on under "Lower during games" get *Below normal*. Everything is restored when
you leave the game, turn Boost off or exit. It shares CPU time only: a GPU-heavy job such as AI
training still competes with the game for the graphics card.

**Automatic gaming mode.** About 5 seconds after a game starts, Windows switches to *Best performance*
and Game Boost turns on (the overlay shows a GAMING MODE badge). About 20 seconds after you stop playing,
both go back to how they were — so a quick alt-tab doesn't flip it. A game is a window rendering 30+ FPS
that is either fullscreen or on your games list (Optimizer › Automatic gaming mode; match part of the
window title or the app name, e.g. `Starve.io`). If the overlay exits mid-game, the power mode is put back
the next time it starts. Toggle it in the Optimizer or tray.

**What's using your PC.** Live CPU, GPU and memory per app, with **End** to close one (Windows
system processes can't be ended from here).

## FPS needs administrator

FPS is measured the same way PresentMon and vendor overlays do it: by listening to the
DirectX present events Windows emits (ETW). Windows only allows that from an elevated process.

- **Start with Windows** registers a Task Scheduler logon task with highest privileges, so the overlay
  starts elevated at every logon **without** a UAC prompt. This is the intended setup.
- When started normally, everything except FPS works, and the tray offers *Restart as administrator*.
- **Standard (non-admin) accounts:** have an administrator add you to the *Performance Log Users*
  group once, then sign out and back in. FPS then works with no elevation at all:

  ```
  net localgroup "Performance Log Users" <your-username> /add
  ```

FPS is shown for whichever window is in the foreground. It covers DirectX 9/10/11/12 games and
Vulkan/OpenGL games that present through DXGI.

## Limitations

- Like any non-injected overlay, it shows over **borderless/windowed** games, not over
  *exclusive* fullscreen. Most modern games default to borderless.
- CPU temperature comes from the ACPI thermal zone (no kernel driver needed). On laptops this
  tracks the CPU package closely. On some desktops it can read low.
- GPU stats require an NVIDIA GPU (via NVML, which comes with the driver).

## Build

```
dotnet build -c Release
```

Output: `bin\Release\net8.0-windows\PCSpecOverlay.exe`. If you move the exe, toggle
*Start with Windows* off and on again so the logon task points at the new location.
