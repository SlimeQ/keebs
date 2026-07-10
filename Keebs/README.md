# Keebs

Windows-native on-screen keyboard prototype with a local prediction strip and
sensitive-field suppression.

## Run

```powershell
dotnet run --project .\Keebs.csproj
```

## Test

From the repository root:

```powershell
dotnet test .\Keebs.slnx
```

The current regression tests cover the Win32 `SendInput` `INPUT` struct layout. This
catches the crash where key taps failed because the x64 P/Invoke struct size did not
match Windows' native layout.

## Installer

From the repository root:

```powershell
dotnet tool restore
dotnet wix eula accept wix7
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The MSI is written to:

```text
artifacts\installer\Keebs-Setup-win-x64.msi
```

The installer places `Keebs.exe` under `Program Files`, adds a Start Menu shortcut,
and uses `Assets\keebs.ico` for the executable, shortcut, and Apps & Features entry.

## Icon Pipeline

The source bitmap is `Assets\keebs-icon-source.png`. Regenerate the app icon assets
from the repository root with:

```powershell
python .\tools\postprocess_icon.py `
  --input .\Keebs\Assets\keebs-icon-source.png `
  --png .\Keebs\Assets\keebs-icon.png `
  --ico .\Keebs\Assets\keebs.ico
```

## Current behavior

- Floating, topmost WPF keyboard window.
- Uses `SendInput` so key taps go to the currently focused app.
- Supports mouse or touch swipe typing across letter keys, resolved locally from
  bundled and learned vocabulary.
- Marks the keyboard window as no-activate so clicking keys should not steal focus.
- Updates predictions from physical key-down events so hardware keyboard typing
  works even when an app does not expose focused text through UI Automation.
  UI Automation is used for focus/caret seeding where available, but physical
  typing keeps its local prediction session so bad accessibility text cannot
  overwrite active suggestions. Keebs still ignores its own injected key events.
- Shows four local suggestions from a local predictor seeded with common words,
  contractions, and a bundled starter corpus.
- Right-click a suggestion and choose `Remove from suggestions` to suppress it
  persistently from the local profile.
- Learns typed words and accepted suggestions into a local profile.
- Suppresses predictions and learning in sensitive fields detected via UI Automation
  password metadata or keywords such as password, PIN, CVV, OTP, and recovery code.
- Also suppresses predictions and learning when focused terminal text looks like
  a credential prompt, such as SSH password or private-key passphrase prompts.
- The prediction switch disables predictions and learning manually.
- Press `Ctrl+Space` while typing on a physical keyboard to accept the first
  visible prediction. Keebs handles the chord before Windows sees it, so repeated
  `Ctrl+Space` does not invoke the system clipboard/share shortcut.
- Existing prediction profiles are versioned and migrated on first launch after
  upgrade while preserving learned local frequencies and merging them at runtime
  with the bundled language base.
- Known browser accessibility artifacts such as Firefox's stray `xhtml` context
  are rejected and removed from migrated prediction profiles.
- Press `Update` to check the latest GitHub release, download the attached MSI,
  and launch the installer.
- Press `Test` to open the local typing-run harness. Finished runs are appended
  as JSONL to `%LOCALAPPDATA%\Keebs\typing-runs.jsonl` for later tuning. Swipe
  commits append trace and candidate diagnostics to
  `%LOCALAPPDATA%\Keebs\swipe-traces.jsonl`.

## Releases

GitHub releases are built automatically from version tags in the canonical
`SlimeQ/keebs` repository.

```powershell
git tag v0.1.37
git push origin main --tags
```

The release workflow runs tests, builds `Keebs-Setup-win-x64.msi`, and attaches
the MSI to the GitHub release. The in-app updater checks
`https://github.com/SlimeQ/keebs/releases/latest`.

## Known limits

- This does not replace the Windows secure-desktop OSK for sign-in or UAC prompts.
- Typing into elevated apps will require a signed `uiAccess` build installed under a
  trusted location such as `Program Files`.
- The predictor is intentionally simple and should be replaced with an ONNX-backed
  local model for SwiftKey-style ranking.
