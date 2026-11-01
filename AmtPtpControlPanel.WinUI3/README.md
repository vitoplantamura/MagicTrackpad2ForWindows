# Magic Trackpad 2 Control Panel (WinUI 3)

A modern WinUI 3 re-implementation of the WinForms `AmtPtpControlPanel`.
It keeps the exact same driver/registry behavior but replaces the UI with a
clean, theme-aware Windows 11 style layout (Mica backdrop, cards, modern
controls).

## Build requirements

- Windows 10 1809+ or Windows 11
- Visual Studio 2022 with the **.NET desktop development** workload
- **Windows App SDK** (the project uses the `Microsoft.WindowsAppSDK`
  NuGet package, so it is restored automatically)
- .NET 8 SDK

## Build & run

1. Open `AmtPtpControlPanel.WinUI3.csproj` in Visual Studio 2022.
2. Select an **x64** configuration (the driver talks to x64 system services).
3. Build and run.

The app is configured as an **unpackaged, self-contained Windows App SDK**
application and keeps the `requireAdministrator` manifest, because it writes to
`HKLM` and talks to the trackpad driver control devices.

> If your machine already has the matching Windows App SDK runtime installed and
> you prefer framework-dependent deployment, set
> `<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>` in the
> `.csproj`.

## Publish (self-contained single exe)

The publish profiles are configured for a **single-file** build. The result is
a single `AmtPtpControlPanel.WinUI3.exe`. The translations XML is embedded in
the executable, and the UI language is persisted in
`HKCU\Software\AmtPtpControlPanel`.

The exe is self-contained, so it runs on a clean Windows 10/11 machine with no
.NET or Windows App SDK install. On first launch it extracts the bundled
WinUI/native libraries to a temp folder, so startup is slightly slower.

> Note: WinUI 3 single-file publishing is a community technique and is not
> officially supported by Microsoft.

### Visual Studio

1. Right-click the project → **Publish**.
2. Choose the **win-x64** or **win-arm64** folder profile
   (`Properties\PublishProfiles\win-x64.pubxml` or `win-arm64.pubxml`).
3. Click **Publish**.

Output folders: `bin\publish\win-x64\` and `bin\publish\win-arm64\`.

### Command line

```powershell
# x64 (uses Properties\PublishProfiles\win-x64.pubxml)
dotnet publish AmtPtpControlPanel.WinUI3.csproj -p:PublishProfile=win-x64

# ARM64
dotnet publish AmtPtpControlPanel.WinUI3.csproj -p:PublishProfile=win-arm64
```

## Project layout

| File | Purpose |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | WinUI 3 application entry point |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | Main settings window and UI logic |
| `SettingsService.cs` | Reads/writes the UMDF + HID filter registry parameters |
| `DeviceInterop.cs` | Named-pipe (USB) and `DeviceIoControl` (Bluetooth) calls |
| `LocalizationManager.cs` | Loads embedded translations and persists the UI language in the registry |
| `app.manifest` | Admin elevation and DPI settings |

## Notes

- The original WinForms project is left untouched in `../AmtPtpControlPanel`.
- Translations are embedded; to change them, edit
  `AmtPtpControlPanel.Localization.xml` and rebuild.
