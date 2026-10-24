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

## Priorities from this audit

1. Version control + tests before larger changes.
2. Safety: session-scoped process handling, no hardcoded restore, persisted Game Boost recovery, settings validation, remove the run-as-other-user path.
3. Separate the installed app from the dev build (startup task currently runs the dev build).
4. Then: telemetry hardening (dGPU sleep), gaming-mode toggles + emergency restore, overlay/control-center polish, history/diagnostics, installer.
