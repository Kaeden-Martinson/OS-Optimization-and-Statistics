# PC Spec Overlay

A lightweight Windows performance overlay and optimizer, in the spirit of NVIDIA's in-game overlay:
live FPS, frame time and 1% lows (DirectX, Vulkan and OpenGL); GPU and CPU load, temperature, clocks and power; slowdown
warnings; and an automatic gaming mode that changes Windows settings only while you play — and puts
them back afterwards.

Everything it changes is recorded, reversible, and never overwrites a choice you made later.

---

## Install

1. Run **`PCSpecOverlay-Setup-<version>.exe`**.
2. Choose whether it should start with Windows, then **Install**.

It installs for your account only (`%LocalAppData%\Programs\PCSpecOverlay`), needs no administrator
rights, and adds a Start menu shortcut and an entry in **Settings › Apps**.

**Requirements:** Windows 10 or 11 (64-bit) and the **.NET 8 Desktop Runtime**. If the runtime is
missing, Windows offers to download it from Microsoft the first time you start the app.

**Updating:** run the newer setup. A running copy is closed first (cleanly, restoring anything it
changed), settings are kept, and the previous version is kept beside the new one as a fallback.

**Uninstalling:** Settings › Apps › PC Spec Overlay › Uninstall. You can choose to undo the Windows
changes the app made and to delete your settings. The startup entry, shortcut and Apps entry are
always removed.

## Using it

| | |
|---|---|
| Show / hide the overlay | **Alt+Shift+O** (change it in Control Center › General) |
| Open the Control Center | Click the tray icon, or start the app again from the Start menu |
| Stop all optimizations immediately | Tray › *Stop optimizations now*, or Overview › *Stop optimizations now* |
| Move the overlay | Control Center › Overlay › Display / Corner — or turn off *Click-through* and drag it; it snaps to the nearest corner of any display |

### Control Center pages

- **Overview** — live tiles, 10-minute FPS and temperature graphs, gaming-mode status and exactly
  which optimizations are active, power plan and mode, last gaming session.
- **Optimizer** — performance-related Windows settings with the current value, the recommendation,
  the benefit and the drawback. Nothing changes until you press a button; every change has **Undo**.
  Also lists what's using your CPU, GPU and memory, with *Lower during games* and *End* (asks first).
- **Gaming** — automatic gaming mode, which optimizations it may use, timing (switch-on delay and
  Alt+Tab grace period), your games list with per-game options, recently seen games.
- **Overlay** — layout (expanded or one-line compact), display, corner, opacity, text size, which
  metrics to show, refresh rates, click-through.
- **Temperatures & alerts** — warning and hot levels for GPU and CPU, notification rules.
- **General** — Start with Windows, the overlay shortcut, GPU sleep, history, restore defaults.
- **Diagnostics** — what works on this PC and why, every Windows change the app currently owns,
  recent events, a copyable report (personal paths replaced), and *Undo every change the app made*.

## Permissions

The app runs as you, without elevation, and never asks for or stores an administrator password.

**FPS needs one permission.** Frame rates come from Windows' DirectX event tracing, which Windows only
lets administrators and members of the **Performance Log Users** group read. On a standard account,
an administrator adds you once:

1. Open **Computer Management › Local Users and Groups › Groups › Performance Log Users**, add your
   account (or run `net localgroup "Performance Log Users" <username> /add` in an administrator terminal).
2. **Sign out and back in** (locking the screen isn't enough — group membership is read at sign-in).

Everything else works without it; the overlay and Overview explain when FPS is unavailable.
On an administrator account, *Start with Windows* registers a "highest available" task (one UAC
approval when you turn it on) so FPS works without a prompt at each sign-in.

Nothing the app changes needs administrator rights: all settings it writes are per-user (HKCU) or
the per-user power mode, and it only touches processes in your own Windows session.

## What the numbers mean

| Metric | Source | Updated | Needs | Accuracy / notes | When unavailable |
|---|---|---|---|---|---|
| FPS | Present events via ETW: **DirectX** (DXGI + Direct3D 9) per swap chain, and **Vulkan/OpenGL** from the graphics kernel (DxgKrnl Present_Info) per window, used only for apps that send no DirectX presents | 0.25–1 s (setting) | Admin or Performance Log Users | Frames presented in the last second ÷ the time they spanned. Browsers present everything through one GPU process; per-swap-chain counting stops videos or other windows inflating the game's number | "—" when the app hasn't presented for 2 s (paused, minimized, menus that don't redraw); Windows delivers events in ~1 s batches, so allow up to ~3 s |
| Frame time | Same | Same | Same | Average interval over the last second | "—" |
| 1% low | Same | Same | Same | 1000 ÷ the 99th-percentile frame interval over the last **10 s** — the frame rate of the slowest 1% of frames | "—" |
| GPU load, temperature, clock, power, VRAM | NVIDIA NVML (driver) | 0.5–5 s (setting) | Nothing | As reported by the driver | Hidden without an NVIDIA GPU; "Sleeping" while a laptop GPU is powered down; "Driver not responding" during driver resets (reconnects automatically) |
| GPU slowdown | NVML clock-limit reasons | Same | Nothing | Driver-confirmed: heat (software/hardware thermal slowdown) or power cap | — |
| CPU load | `GetSystemTimes` | Same | Nothing | All cores, since the previous sample | "—" |
| CPU temperature | Hottest ACPI thermal zone | Same | Nothing | Tracks the CPU package closely on laptops; not a per-core sensor | "—" if the PC exposes no thermal zones |
| CPU clock, CPU slowdown | Windows *Processor Information* counters (`% Processor Performance`, `% Performance Limit`) | Same | Nothing | Effective clock incl. turbo; a cap below 95% while ≥90 °C counts as heat, otherwise power | — |
| RAM | `GlobalMemoryStatusEx` | Same | Nothing | Exact | — |

Missing readings are always shown as "—" or a word, never as a fake zero. Sensors are only read while
the overlay is visible, the Control Center is open, alerts are on, or gaming mode is active.

**Laptop GPU sleep.** Asking the NVIDIA driver for sensors keeps an Optimus laptop's GPU powered. The
app first checks the GPU's power state through Windows' device manager (which doesn't wake it) and
skips the driver while it's asleep (General › *Let the laptop GPU sleep*).

## Automatic gaming mode

A window counts as a **game** when it renders at 30+ FPS in front **and** matches your games list
(by part of the window title — e.g. `Starve.io` for a browser game — or by app name). Unlisted
fullscreen apps are shown as detected but aren't optimized unless you add them (or turn on
*Also optimize unlisted fullscreen apps*), because a fullscreen video would look the same.

After a game has been in front for the switch-on delay (default 5 s) it applies what you enabled:

- **Power mode → Best performance** (the per-user power mode on top of your power plan; the plan itself is never changed)
- **Game Boost**: the game's processes go from Normal to Above normal; apps you chose go to Below normal.
  One step only, never High or Realtime, never Windows system processes, only your session.

When no game has been in front for the grace period (default 20 s), everything is put back.

**Safety rules** (all covered by automated tests):

- The original value is recorded before every change and the result is verified.
- A value is only restored if it still holds what the app set. If you changed it yourself in the
  meantime, your newer choice is kept (and the Overview says so).
- Several games are tracked at once; nothing is restored while another still needs it. A manual
  Optimizer fix and gaming mode can share a change; it's only undone when neither needs it.
- If an optimization isn't available (e.g. a power plan without power modes) it's reported and
  skipped; the rest still apply.
- If the app crashes or is ended mid-game, the next start puts back what gaming mode and Game Boost
  changed (process priorities are matched by process ID **and** start time, so a reused ID is never touched).
- **Recovery mode:** if the app fails to start normally three times in a row, automatic
  optimizations stay off until you turn them back on.

## Browser graphics preference

Optimizer › *Edge/Chrome graphics chip* sets Windows' per-app graphics preference (Settings ›
Display › Graphics) so the browser and browser games use the NVIDIA GPU. It takes effect after the
browser restarts; the app tracks that across its own restarts and clears the warning if you undo the
change before restarting.

*Restart Edge* only ever runs after you confirm. It sends Edge the same "session ending" message
Windows sends at shutdown (Edge saves every window and tab), waits for it to exit, and reopens it
with `--restore-last-session`. If Edge doesn't close (e.g. a "Leave site?" prompt) nothing is forced;
if it doesn't reopen, the app says so and stops offering the restart until the browser runs again.

## Files

| | |
|---|---|
| `%AppData%\PCSpecOverlay\settings.json` | Your preferences (validated on load; a damaged file is kept aside as `settings.json.corrupt-*`) |
| `%AppData%\PCSpecOverlay\state.json` | Changes the app currently owns and how to undo them |
| `%AppData%\PCSpecOverlay\overlay.log` | Log, rotated at 1 MB |
| `%AppData%\PCSpecOverlay\sessions\` | One small summary per gaming session, deleted after the retention period |

Nothing is uploaded anywhere.

## Troubleshooting

- **FPS shows "—" in a game** — the game may be paused or minimized. Exclusive-fullscreen games
  may also hide the overlay (borderless/windowed is fine).
- **FPS unavailable everywhere** — see *Permissions*; Diagnostics shows whether frame monitoring runs.
- **Hotkey doesn't work** — another app may own it; Control Center › General shows the active
  shortcut and lets you pick another (the old one stays active if the new one is taken).
- **Something changed that you didn't expect** — Diagnostics lists every change the app owns; use
  *Undo every change the app made* or Undo on the Optimizer page.
- **Need help** — Diagnostics › *Copy diagnostic report*.

## Limitations

- Not code-signed (no signing certificate is available), so Windows SmartScreen may warn on first run.
- No online update check: there is no release server. Run a newer setup to update.
- FPS covers DirectX 9–12, browsers, Vulkan and OpenGL. Games on older APIs that bypass both
  DirectX and the Windows graphics kernel's present path (e.g. some DirectDraw titles) aren't counted.
- CPU temperature is the hottest ACPI thermal zone, not per-core sensors.
- Browser restart was verified with one Edge profile open; with windows from several profiles,
  the confirmation warns that some may need reopening from History.
- The overlay draws with the CPU (keeps memory low and lets a laptop GPU sleep); expect roughly
  0.5–2.5% of one CPU core while visible, less when hidden.

## Development

```
dotnet build PCSpec.slnx -c Release
dotnet test PCSpecOverlay.Tests -c Release
dotnet publish PCSpecOverlay -c Release -p:PublishSingleFile=true -o artifacts\publish
```

Rename the published exe to `PCSpecOverlay-Setup-<version>.exe` to make the setup program (a file
whose name contains "Setup" runs the installer). Builds running from anywhere other than the install
folder are **development builds**: they use `%AppData%\PCSpecOverlay-Dev`, their own startup entry,
lock and monitoring session, so they never interfere with the installed app.

`docs/AUDIT.md` records the starting audit and the verification of each feature.
