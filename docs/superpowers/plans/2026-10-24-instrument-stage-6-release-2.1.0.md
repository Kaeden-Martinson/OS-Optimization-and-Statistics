# Instrument Stage 6: Release 2.1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship everything on local main as **Optiframe 2.1.0**: the Instrument redesign (stages 1–5), the Optiframe name and logo, and move-and-pin. It's published on GitHub so that the installed 2.0.13 offers it as an update. The update is installed through the app's own updater, the result is checked on the PC, and the user's overlay layout is switched to Strip.

**Architecture:** One code task, then operations. The code task bumps the version, adds a "What's new" file for 2.1.0, and teaches `build-release.ps1` to put that file above the install line and the SHA-256 line in the release notes. The operations reuse the 2.0.13 path:
1. `build-release.ps1` (tests, single-file publish, notes with SHA-256).
2. Push main and a `v2.1.0` tag.
3. Create the GitHub release with the setup file and the notes (Claude in Chrome, already signed in to GitHub).
4. Update from inside the installed 2.0.13.

Every step that leaves this PC waits for the user's explicit go-ahead.

**Tech Stack:** .NET 8, PowerShell (`build-release.ps1`), git, GitHub Releases (web UI through Claude in Chrome; no `gh` CLI), xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-10-24-instrument-redesign-design.md` §4 step 6 ("Release 2.1.0 with build-release.ps1, published to GitHub; the installed 2.0.13 offers it as an update") and §3 "In-game overlay" (the user's saved `Expanded` is switched to Strip through the setting after 2.1.0 is installed). Also the Optiframe spec (`2026-10-24-optiframe-name-logo-placement-design.md`), whose rename ships with 2.1.0.

## Global Constraints

- **Version** `2.1.0` (csproj `<Version>`), tag `v2.1.0`, setup file `Optiframe-Setup-2.1.0.exe`. Not code-signed: the user decided against signing, so don't suggest a certificate. The release notes keep exactly one `SHA256: <64 hex>` line, which the 2.0.13 updater needs: it reads the first `SHA-?256 [:=] <hex>` match and checks the download's size and hash against it.
- **Compatibility with the installed 2.0.13.** Its updater picks the release asset whose name contains "Setup" and ends in ".exe". It saves the download as `PCSpecOverlay-Setup-2.1.0.exe`, and setup is recognized by "Setup" in the file name. Nothing may break that path.
- **Outward actions need the user's explicit go-ahead in chat, each time.** This covers pushing to GitHub, pushing the tag, creating or publishing the release, uploading the asset, and installing the update on the user's PC. Before asking, state what will be sent where. Never force-push. Never delete a published release.
- **Release notes.** "What's new" in plain words. Mention:
  - the new name;
  - the redesign with light/dark and Match Windows as the default;
  - the Strip overlay as the default for fresh installs;
  - moving and pinning the overlay;
  - per-change Undo;
  - that temperature levels are now kept between 30 and 100 °C (Warning below Hot), so higher saved values are lowered;
  - that the Start-menu shortcut is renamed;
  - that settings carry over.
- **Build and tests.** `dotnet build PCSpec.slnx` must have 0 warnings, and `dotnet test PCSpec.slnx` must pass (274 tests pass before this plan). Shell examples use Git Bash from the repo root, except `build-release.ps1`, which runs in PowerShell.
- **Commits** end with `Co-Authored-By: <the model that wrote it> <noreply@anthropic.com>`.

---

### Task 1: Version 2.1.0 and release notes

**Files:**
- Modify: `PCSpecOverlay/PCSpecOverlay.csproj` (`<Version>`), `build-release.ps1`
- Create: `docs/releases/2.1.0.md`
- Test: `PCSpecOverlay.Tests/UpdateTests.cs` (append a class)

- [ ] **Step 1: Write the failing test**

Append to `PCSpecOverlay.Tests/UpdateTests.cs`:

```csharp
public class ReleaseNotesTests
{
    [Fact]
    public void Version_2_1_0_has_whats_new_and_the_script_includes_it()
    {
        var csproj = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/PCSpecOverlay.csproj"));
        Assert.Contains("<Version>2.1.0</Version>", csproj);

        var notes = File.ReadAllText(SourceFiles.Path("docs/releases/2.1.0.md"));
        foreach (var topic in new[] { "Optiframe", "light", "Strip", "pin", "Undo", "30", "100 °C", "Start menu", "settings" })
            Assert.Contains(topic, notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"SHA-?256\s*[:=]", notes); // the script adds the one checksum line the updater reads

        var script = File.ReadAllText(SourceFiles.Path("build-release.ps1"));
        Assert.Contains(@"docs\releases\$version.md", script);
        Assert.Contains("SHA256: $hash", script);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run `dotnet test PCSpec.slnx --filter "FullyQualifiedName~ReleaseNotesTests"`. Expected: 1 failed (the version is still 2.0.13).

- [ ] **Step 3: Implement**

1. `PCSpecOverlay/PCSpecOverlay.csproj`: `<Version>2.0.13</Version>` becomes `<Version>2.1.0</Version>`.
2. Create `docs/releases/2.1.0.md`:

```markdown
### What's new

**PC Spec Overlay is now Optiframe.** New name and logo everywhere you see them: the tray, the window, the installer and the Start menu. The Start-menu shortcut is renamed to Optiframe, and the old one is removed. Your settings, history and Start with Windows carry over; nothing to redo.

**A new look, light or dark.** The whole app is redesigned: a tab bar instead of the sidebar, an Overview with a frame-rate chart and what the app did and why, and every page rebuilt as clear ruled tables. Choose Light, Dark or Match Windows (the default) on the Settings page or with the sun/moon switch in the tab bar. Color now only means something: amber for a warning, red for hot.

**The overlay.** Two layouts: Strip, one ruled line and the default for new installs, and Panel, with a live frame-time trace. Use Move overlay (tray or Overlay page) to drag it anywhere on any screen, then press Pin; clicks pass through to your game whenever it's pinned. The Overlay page shows a live preview; click a corner to put it there.

**Temperatures & alerts.** Drag the warning and hot levels along a 30–100 °C scale (arrow keys work too). Levels are now always kept between 30 and 100 °C with Warning below Hot, so a level you had set higher than 100 °C is lowered to 100.

**Diagnostics.** A health grid of what works on this PC, and a change journal: every Windows setting the app currently has changed, who changed it, and an Undo for each one. A setting is only put back if it's still what the app set; if you changed it yourself since, your choice is kept.

**Also:** every control has a screen-reader name; the tray menu and installer follow the new look.
```

3. `build-release.ps1`: just before the `@" … "@ | Set-Content "$out\release-notes.md"` block, add:

```powershell
# What's new for this version, written by hand, goes above the install line and the checksum
$whatsNewFile = "docs\releases\$version.md"
$whatsNew = if (Test-Path $whatsNewFile) { (Get-Content $whatsNewFile -Raw).Trim() + "`n`n" } else { "" }
```

   Then change the notes template so it reads:

```powershell
@"
## Optiframe $version

$whatsNew
Run ``Optiframe-Setup-$version.exe`` to install or update (per-user, no administrator rights).
Requires Windows 10/11 64-bit and the .NET 8 Desktop Runtime.

SHA256: $hash
"@ | Set-Content "$out\release-notes.md" -Encoding UTF8
```

   Also update the `.EXAMPLE` help text: one line saying that `docs\releases\<version>.md` is included when present.

- [ ] **Step 4: Run to verify pass**

Run the Step 2 filter: expected pass. Then run `dotnet build PCSpec.slnx` (0 warnings) and `dotnet test PCSpec.slnx` (all pass).

- [ ] **Step 5: Commit**

```bash
git add PCSpecOverlay/PCSpecOverlay.csproj build-release.ps1 docs/releases/2.1.0.md PCSpecOverlay.Tests/UpdateTests.cs
git commit -F- <<'EOF'
Version 2.1.0 with a What's new section in the release notes

Co-Authored-By: <model> <noreply@anthropic.com>
EOF
```

Then merge to main the usual way: review, then the user's OK to merge locally. The following steps run on main.

---

### Task 2: Build the release (controller, on this PC)

- [ ] **Step 1:** Close any development copy. In PowerShell from the repo root, run `.\build-release.ps1`. It runs the tests in Release and stops if any fail.
- [ ] **Step 2:** Check the output:
  - `artifacts\release\Optiframe-Setup-2.1.0.exe` exists (about the size of 2.0.13's setup, roughly 70–80 MB; read the 2.0.13 asset size from the GitHub release page to compare).
  - "Signed: no".
  - "Updates: checks github.com/Kaeden-Martinson/OS-Optimization-and-Statistics".
  - `artifacts\release\release-notes.md` has the What's new section, the install line, and exactly one `SHA256:` line.
- [ ] **Step 3: Smoke test the setup without installing.**
  - Start `Optiframe-Setup-2.1.0.exe` and capture its window with PrintWindow. It should read "Update Optiframe": "Version 2.0.13 is installed; this will replace it with 2.1.0".
  - Close it with Cancel through UI Automation. Nothing is installed before the main button.
  - Don't use the desktop screenshot tool on the app's windows: it hides and resizes them.
- [ ] **Step 4:** Note the size and SHA-256 for the ledger.

### Task 3: Publish (controller; each step only after the user's explicit go-ahead)

- [ ] **Step 1: Ask.** "Push main (N commits ahead of origin) and tag v2.1.0 to github.com/Kaeden-Martinson/OS-Optimization-and-Statistics?" State N from `git rev-list --count origin/main..main`. On yes: `git tag v2.1.0`, `git push origin main`, `git push origin v2.1.0`. Commits already use the user's no-reply email; check with `git log --format=%ae origin/main..main | sort -u`.
- [ ] **Step 2: Ask.** "Create the GitHub release v2.1.0 with Optiframe-Setup-2.1.0.exe (size, SHA-256) and the notes from artifacts\release\release-notes.md, published (not a draft)?" On yes, in Claude in Chrome (signed in as the user):
  1. Go to the repo's Releases, then New release.
  2. Pick tag v2.1.0, title "Optiframe 2.1.0", and paste the notes.
  3. Upload the setup file and publish.
- [ ] **Step 3: Check it.**
  - The release page shows the asset with the right size.
  - `https://api.github.com/repos/Kaeden-Martinson/OS-Optimization-and-Statistics/releases/latest` returns `tag_name` v2.1.0, the asset, and notes containing the SHA-256.

### Task 4: Update the installed copy and finish (controller, with the user)

- [ ] **Step 1: Ask the user to update from the installed 2.0.13.** They choose Settings, then Check now, then Download and install, and confirm in the dialogs. Don't click it for them, because it replaces the installed app. In 2.0.13 the Settings page is the "General" page, laid out the old way.
- [ ] **Step 2: Verify.**
  - `%LocalAppData%\Programs\PCSpecOverlay\PCSpecOverlay.exe` has file version 2.1.0.
  - The log shows "Starting 2.1.0".
  - The Start menu has "Optiframe.lnk" and no longer has "PC Spec Overlay.lnk".
  - The Apps & Features entry reads Optiframe 2.1.0.
  - The startup task still points at the installed exe.
  - `%LocalAppData%\Programs\PCSpecOverlay` keeps the fallback copy of 2.0.13.
- [ ] **Step 3: Switch the layout to Strip.** The user picked Strip in brainstorming, and the spec says to switch through the setting. Ask first. On yes, have the user (or UI Automation, with their OK) select the Strip card on the Overlay page. Check that `%AppData%\PCSpecOverlay\settings.json` has `"Layout": "Compact"`.
- [ ] **Step 4: Record it.** Add a "Release 2.1.0" section to `PCSpecOverlay/docs/AUDIT.md`: build output, release URL, update result, the checks above, and the layout switch. Commit and, with the user's OK, push.
- [ ] **Step 5: Offer the still-pending restart check.** Does the installed copy start on its own after a real restart? Look for "Starting" in `%AppData%\PCSpecOverlay\overlay.log` after the logon time.
