# Stage A audit — 2026-10-23

Baseline before the professional-upgrade work. Every "Verified" entry was checked on this PC
(Windows 11 Home 26200, i9-13900H, RTX 5060 Laptop + Iris Xe, standard user account).

Legend: ✅ verified working · 🟡 partially working / works with caveats · ❌ broken · ⬜ not yet tested

## Build and runtime

| Item | State | How it was verified |
|---|---|---|
| Builds (Release, .NET 8, 0 warnings) | ✅ | `dotnet build -c Release` |
| Launches and stays responsive | ✅ | Running since 22:48 via logon task; `Responding=True` |
| Resource use | ✅ | 30 s sample: 0.42 % of one core, 116 MB private, 723 handles, 15 threads |
| Single instance; second launch opens Optimizer | ✅ | Launched exe twice → Optimizer window appeared in the running instance |
| Version control | ❌ | No git repository |
| Automated tests | ❌ | None exist |
| Logging | 🟡 | Append-only `overlay.log`, no levels, no rotation (470 B today, but unbounded) |
| Settings validation | ❌ | A corrupt `settings.json` is silently replaced with defaults on the next save; values aren't range-checked |

## Metrics

| Metric | State | Source / notes |
|---|---|---|
| FPS, frame time, 1 % low | ✅ (DXGI/D3D9) | ETW present events per swap chain. Matched an independent ETW counter (overlay 163–165 vs measured 153.6 avg). Vulkan/OpenGL games that don't present through DXGI are not covered |
| FPS on a standard account | ✅ | Token has `BUILTIN\Performance Log Users` (enabled); `PCSpecOverlay-Presents` session is running without elevation |
| GPU load/temp/clock/power/VRAM | ✅ | NVML; values match `nvidia-smi` |
| GPU slowdown reasons | ✅ | NVML throttle reasons; observed `0x20` (SW thermal) at 86 °C |
| CPU load | ✅ | `GetSystemTimes` deltas |
| CPU temperature | 🟡 | Hottest ACPI thermal zone, not a per-core sensor; labelled "CPU" |
| CPU clock / performance limit | ✅ | `Processor Information` counters |
| RAM | ✅ | `GlobalMemoryStatusEx` |
| Unavailable vs zero | 🟡 | Unavailable shows "—", but a GPU with no driver and a GPU that failed to read look the same |
| Laptop dGPU sleep | ❌ | NVML is polled every second regardless of whether the GPU is asleep |
| ETW event cost | 🟡 | Each present event copies its payload into a new array |

## Overlay and controls

| Item | State | How it was verified |
|---|---|---|
| Global hotkey Alt+Shift+O | ✅ | Synthesized keypress toggled the overlay off and on; a second `RegisterHotKey` for the same combo is refused (we hold it) |
| Hotkey conflict handling | 🟡 | Tray notification if registration fails; can only be changed by editing JSON |
| Click-through, no focus, hidden from Alt+Tab | 🟡 | Extended styles set in code; games kept focus in every test. No way to turn click-through off |
| Multi-monitor / mixed scaling | 🟡 | Always on the primary monitor's work area (correct there); no monitor choice. Optimizer window opened off-screen on the 100 %-scaled external monitor until fixed |
| Tray icon and menu | 🟡 | Menu actions exercised through the same code paths; menu clicks themselves not automated |
| Layout options (compact, metrics, refresh rate, thresholds) | ❌ | Not implemented; thresholds hardcoded |

## Startup and permissions

| Item | State | How it was verified |
|---|---|---|
| Start with Windows (logon task) | 🟡 | `\PCSpecOverlay` task exists, runs as the signed-in standard user, LeastPrivilege, 3 s delay, battery-safe; launched the app after the earlier reboot. **Points at the OneDrive build folder**, so every rebuild replaces the "installed" app |
| Task creation from the tray on a standard account | ⬜ | The current task was registered manually with the same XML after the tray path failed (fixed since, but the tray path itself is untested) |
| Duplicate startup entries | ✅ | Exactly one task; no Run-key entry |
| No repeated elevation prompts | ✅ | Runs `asInvoker`; nothing prompts at logon |
| "Restart as administrator" | ❌ | On this standard account it asks for another user's (blmar's) password and runs the app as that user with that user's settings — confusing and unnecessary now that the group membership works |

## Optimizer and gaming mode

| Item | State | How it was verified |
|---|---|---|
| Fix / Undo for power mode, browser GPU, Game Mode, background recording | ✅ | Fix then Undo via UI Automation; registry and power APIs read back the original values |
| Power-mode naming | ❌ | Shows "Balanced" for the slider position while the active plan is the Acer OEM plan (`93bab788…`) |
| Power-mode Undo fallback | ❌ | If the saved value can't be parsed it restores the hardcoded Balanced position |
| Browser restart-required banner | ✅ | Appeared for Edge started before the change; persisted across app restart; cleared after restart |
| Browser restart preserving windows/tabs | 🟡 | Real Edge: 1 window, 2 tabs restored, moved to NVIDIA GPU. Throwaway profile: 3 windows restored. Not tested with two Edge profiles open at once (this PC has two profiles) |
| Browser process detection | ❌ | Uses process name only — would also match browsers in other users' sessions |
| Chrome | 🟡 | Installed but crashes on launch (exit −1 outside this app). Its GPU preference was changed during testing; a restart would fail with no explanation |
| Automatic gaming mode | ✅ | Starve.io: switched to Best performance + Game Boost after 5 s, restored the original power mode after 20 s, survived a quick alt-tab; ChatGPT tab and animated Claude app correctly ignored |
| Gaming mode recovery after crash | 🟡 | Power mode is persisted and restored at next start (only if still Best performance). **Game Boost priority changes are not persisted** — a crash leaves processes at changed priorities until they exit |
| Respecting user changes during gaming | 🟡 | Won't restore if you moved off Best performance yourself, but doesn't tell you |
| Per-optimization toggles, emergency restore | ❌ | Not implemented |
| Game Boost process matching | ❌ | By name only, across all sessions |

## Leftovers from earlier testing

- Throwaway Edge test profile: already deleted ✅
- No stray test processes, no extra ETW sessions ✅
- Scratch files only in the session scratchpad (outside the project) ✅
- Chrome GPU preference set to NVIDIA while Chrome is broken — harmless, left for the user to decide

## After the upgrade (version 2.0.6, same PC)

| Area | State | How it was verified |
|---|---|---|
| Version control and tests | ✅ | git history from the baseline; 66 unit tests (journal, gaming mode with a simulated clock, frame math, detection, restart tracking, settings, history, hotkeys, startup-task parsing) |
| Crash recovery mid-game | ✅ | Simulated on the real system: power mode left on Best performance and a process at Below normal with "still owned" records → next start restored both and cleared the records |
| Never overwriting a newer choice | ✅ | Real system: record said "restore Balanced" but the mode had since been changed → left as changed, logged why |
| Optimizer Fix/Undo through the journal | ✅ | UI Automation: Fix set Best performance and recorded the original; Undo restored Balanced and cleared the record |
| Installer: install / update / uninstall / reinstall | ✅ | Real system, 2.0.0 → 2.0.6: running copy closed cleanly via its exit signal; previous version kept; settings and undo records survived; uninstall removed folder, shortcut, Apps entry, startup task and monitoring session; reinstall restored all |
| Startup entry points at the installed app | ✅ | Task `\PCSpecOverlay` → `%LocalAppData%\Programs\PCSpecOverlay\PCSpecOverlay.exe`; one task only. Fixed two bugs found on the way (output encoding without a console; XML unescaping on .NET 8) |
| Startup after a real reboot (installed copy) | ⬜ | Needs a sign-out/restart by the user. The same task definition started the app after the earlier reboot |
| FPS on the standard account | ✅ | Installed copy logs "Frame monitoring started" without elevation |
| Global hotkey, conflicts | ✅ | Installed copy toggles; a second copy (dev build) correctly reports the key is taken and keeps working without it |
| Overlay window behavior | ✅ | Extended styles read back: click-through, tool window (no Alt+Tab), no-activate, topmost |
| Accessibility | ✅ | Fixed: page content wasn't exposed to screen readers (15 → 133 automation elements); every control has a name |
| Dev/installed separation | ✅ | Dev build used `%AppData%\PCSpecOverlay-Dev`, `\PCSpecOverlay-Dev` task and `-Dev` ETW session; removed after testing |
| Startup time | ✅ | UI-thread startup ~0.3 s (CPU counters, which took 0.9–5 s, now open in the background) |
| Resource use | ✅ | Measured against the baseline build alternately under the same load: within noise (baseline 0.8–2.1 %, new 1.5–2.3 % of one core; ~100–113 MB both). Drawing is ~2 points of that; monitoring alone is ~0.6 % |
| Automatic gaming mode on the installed copy with Starve.io | ⬜ | Logic covered by tests and verified live on the previous version; the rebuilt version needs a short play session |
| Laptop GPU sleep path | 🟡 | Power-state read verified (reports D0 while the GPU is in use). The asleep (D3) branch couldn't be observed because the AI workload kept the GPU awake |
| Browser restart with several Edge profiles open | 🟡 | Not tested; the confirmation warns when windows from more than one profile are open |
| Vulkan and OpenGL FPS | ✅ | Added in 2.0.9 via the graphics kernel's present event. Automated tests render real frames: OpenGL on Intel ~60/60 (kernel path), Vulkan on Intel 48.3/48 (kernel path), Vulkan on NVIDIA 71.9/72 (driver hands frames to DXGI → DirectX path, not double-counted). On-screen check (2.0.10): the user clicked each test window and the overlay read about 72, 48 and 90 for targets 72, 48 and 90. That check found and fixed a bug where a Vulkan window borrowed a same-named process's frame rate |
| Code signing, online updates | ❌ | No certificate or release server available; documented in the README |

## Instrument stage 1 (2026-10-24, development build on the same PC)

Light/dark foundation for the redesign (spec: `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md`). 135 automated tests pass; these were checked on screen.

| Check | Result | Notes |
|---|---|---|
| Match Windows at start | ✅ | Windows app mode was Light; the app opened light |
| Dark chosen in the app | ✅ | Switched live, title bar included, no restart; square switches and Theme row as designed |
| Optimizer page, dark | ✅ | Primary button filled, amber only on "Balanced", green only on good statuses |
| Light theme readability | ✅ | Overview labels and units use Muted (Faint is no longer used for text); hot CPU reading in red |
| Following Windows live | ✅ | With Match Windows on, the user flipped Windows app mode Dark → Light → Dark; the app followed each time within a second (log 20:45:49, 20:45:57, 20:46:00) |
| In-game overlay | ✅ | Stayed dark in all modes (its own theme setting comes in stage 2) |
| Screen-reader names | 🟡 | The new Theme dropdown is named. 38 older controls have no name (sliders, some dropdowns, slider step buttons); this predates the redesign and is tracked for a separate fix |

## Instrument stage 2 (2026-10-24, Release development build beside the installed 2.0.13)

The in-game overlay rebuilt as Strip (default) and Panel with its own theme. 169 automated tests pass; these were checked on screen.

| Check | Result | Notes |
|---|---|---|
| Panel | ✅ | Header with GPU name and FPS source, frame-time trace, numbers in Cascadia Mono, amber/red only for temperatures and slowdown chips, square corners, no green |
| Strip | ✅ | One ruled line; the background darkens what's behind it as designed (bright desktop 245 → 16–37) |
| Overlay theme | ✅ | Always light switched live; Same as app followed the app's Light/Dark switch live; Always dark is the default |
| In game (Starve.io in Edge) | ✅ | 165 FPS, 6.1 ms, source DXGI; uneven frames show as spikes on the trace; GPU 91 °C and CPU 96 °C in red; readable over the bright game |
| CPU use | ✅ | Same 60 s side by side: installed 2.0.13 1.61 % of one core, new Panel 1.61 % |
| Per-game layout, drag-and-snap | ⬜ | Code path unchanged from before and reviewed; not exercised on screen this time |

## Instrument stage 3 (2026-10-24, Release development build)

Top tab bar and the new Overview. 190 automated tests pass; these were checked on screen.

| Check | Result | Notes |
|---|---|---|
| Tab bar | ✅ | Seven tabs with icons, Overview underlined; app name left, "2.0.13 dev" and the sun/moon switch right |
| Minimum width (900 px) | ✅ | At 125 % scaling the last tab (Diagnostics) ends at 1193 px, clear of the version label; no overlap |
| Overview, dark | ✅ | Four ruled readings with 10-segment bars (GPU 88 °C and CPU 94 °C in red), chart with grid and an amber dip dot, events (shortcut warning in amber), status line with the green light |
| Sun/moon switch | 🟡 | Tooltip "Light theme" shows on hover; the click itself wasn't exercised on screen. The switch logic was reviewed and the Settings dropdown sync was fixed in review |
| Light theme of the new Overview | ⬜ | Not looked at on screen this time |

## Optiframe name, logo and placement (2026-10-24, Release development build)

| Check | Result | Notes |
|---|---|---|
| Overlay size on start | ✅ | Panel at top left, 242×222 px, same as main before the change |
| Name, logo and move-and-pin | ✅ | The user ran the check on the PC (tray icon and name, Move overlay, drag, Pin, Custom position, corner presets, restart) and reported it looks right |
| Note | — | An earlier 159×27 size came from the screenshot tool hiding and restoring the window (WPF drops SizeToContent after an outside resize), not from the app |

## Instrument stage 4 (2026-10-24, Release development build beside the installed 2.0.13)

Pages were switched and captured through UI Automation and PrintWindow, without mouse clicks.

| Check | Result | Notes |
|---|---|---|
| Six pages, dark | ✅ | Optimizer ledger (amber edge on Power mode, "Apply 1 recommendation", GPU preference values wrap), Gaming pipeline, Overlay preview (Panel at top left with live GPU 87°/CPU 92°, Strip/Panel cards), Alerts scales (GPU 87 °C past hot 85, current-reading line visible), Settings status strip, Diagnostics grid ("All 12 checks look fine"), empty journal |
| Six pages, light | ✅ | Same layouts; the selected Appearance segment shows a light icon on the dark fill; the tab-bar sun stays in step |
| Theme chosen through UI Automation | ✅ | Selecting the Light segment through UI Automation saved `Appearance: Light`; Match Windows was restored the same way |
| Screen-reader names | ✅ | Every top-level control on all seven pages has a name. The only unnamed items are slider and scrollbar parts (track buttons and thumbs) inside named sliders |
| Dragging a scale handle, corner clicks, Pause | ⬜ | Not exercised on screen (no mouse input); left to the user |
| Note | — | The screenshot tool hides other apps' windows. It minimized the control center and resized the overlay, so the dev copy was restarted and captured with PrintWindow instead |

## Instrument stage 5 (2026-10-24, Release development build beside the installed 2.0.13)

| Check | Result | Notes |
|---|---|---|
| Installer, update mode | ✅ | `--install` from the dev build, captured with PrintWindow, closed without pressing Update. One card in dark: logo and heading, explanation, Start with Windows row, Cancel and a filled Update. |
| Installer, uninstall mode | ✅ | `--uninstall`: both switch rows and a filled Uninstall (no red). The long "Undo Windows changes…" label was clipped on one line; fixed so it wraps (bd74184), then recaptured. |
| Tray menu | ✅ | The user right-clicked the dev copy's tray icon and reported that it looks right. |
| Menu drawing test | ✅ | A rendering test paints a real menu: palette background and border, check at the right, no stock Windows check under the icon. |

## Priorities from this audit

1. Version control + tests before larger changes.
2. Safety: session-scoped process handling, no hardcoded restore, persisted Game Boost recovery, settings validation, remove the run-as-other-user path.
3. Separate the installed app from the dev build (startup task currently runs the dev build).
4. Then: telemetry hardening (dGPU sleep), gaming-mode toggles + emergency restore, overlay/control-center polish, history/diagnostics, installer.
