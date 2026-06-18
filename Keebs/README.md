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
- Marks the keyboard window as no-activate so clicking keys should not steal focus.
- Shows three local suggestions from a small built-in predictor.
- Learns accepted suggestions in memory for the current session.
- Suppresses predictions and learning in sensitive fields detected via UI Automation
  password metadata or keywords such as password, PIN, CVV, OTP, and recovery code.
- Private mode disables predictions and learning manually.

## Known limits

- This does not replace the Windows secure-desktop OSK for sign-in or UAC prompts.
- Typing into elevated apps will require a signed `uiAccess` build installed under a
  trusted location such as `Program Files`.
- The predictor is intentionally simple and should be replaced with an ONNX-backed
  local model for SwiftKey-style ranking.
