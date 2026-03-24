# ResolutionToggle

A small Windows utility that toggles the primary display between **1920 x 1200 @ 100 % scaling** and the **recommended (native) resolution with default scaling**.

## Features

- One-click toggle between the two display modes.
- Shows the current resolution, scaling percentage, and recommended mode.
- Resolution changes are applied instantly via the Win32 `ChangeDisplaySettings` API.
- DPI scaling overrides are written to the registry (a sign-out / sign-in is needed for Windows to apply the scaling change).
- Per-Monitor DPI-aware so it reads the correct scaling values on multi-monitor setups.
- Dark-themed Windows Forms UI.

## Requirements

| Requirement | Version |
|---|---|
| OS | Windows 10 or later |
| SDK | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| IDE (optional) | Visual Studio 2022 (17.8+) |

## Build

### Visual Studio 2022

1. Open `ResolutionToggle.sln` in Visual Studio 2022.
2. Select **Release** configuration.
3. Build the solution (**Ctrl+Shift+B**).
4. The executable will be at `ResolutionToggle\bin\Release\net8.0-windows\ResolutionToggle.exe`.

### Command line

```bash
dotnet build ResolutionToggle.sln -c Release
```

### Publish a self-contained executable

To produce a single `.exe` that does not require .NET to be installed:

```bash
dotnet publish ResolutionToggle/ResolutionToggle.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The resulting `publish\ResolutionToggle.exe` can be copied anywhere and run directly.

## Usage

1. Run `ResolutionToggle.exe`.
2. The window shows your current resolution, scaling, and the recommended mode.
3. Click the toggle button:
   - If you are **not** at 1920 x 1200 / 100 %, it switches to that mode.
   - If you **are** at 1920 x 1200 / 100 %, it switches back to the recommended resolution with default scaling.
4. Resolution changes apply immediately. Scaling changes require a **sign-out / sign-in** to take effect (this is a Windows limitation).

## How it works

| Action | Mechanism |
|---|---|
| Change resolution | `ChangeDisplaySettings` (user32.dll) with `CDS_UPDATEREGISTRY` |
| Read DPI | `GetDpiForMonitor` (shcore.dll) |
| Set DPI scaling | Registry keys `HKCU\Control Panel\Desktop\LogPixels` and `Win8DpiScaling` |

## License

This project is provided as-is for personal use.
