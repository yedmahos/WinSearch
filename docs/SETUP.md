# Windows Setup.exe distribution

The default project now runs without MSIX identity. The installer targets Windows 11
on native x64, installs per user under `%LOCALAPPDATA%\Programs\WinSearch`, creates
a Start menu shortcut, offers a desktop shortcut, and registers an uninstaller.
It does not request administrator rights or import a signing certificate.

## Build

Use Windows with the .NET 10 SDK and WinUI/XAML build tools (Visual Studio's WinUI
workload or equivalent Build Tools), plus Inno Setup 6.3 or later. NuGet restore
requires network access. Keep the package versions in `WinSearch.csproj` available
in the configured feeds. Inno Setup has its own licensing terms.

From the repository root, in PowerShell:

```powershell
.\scripts\Build-Setup.ps1 -Version 1.0.0 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The script cleans only `artifacts\publish\win-x64`, publishes, validates the payload,
compiles the installer, and writes:

```text
artifacts/installer/WinSearch-Setup-1.0.0.exe
artifacts/installer/WinSearch-Setup-1.0.0.exe.sha256
```

Close any app running from the publish directory before rebuilding. `-Version`
sets both app and installer version. Keep the Inno `AppId` unchanged for upgrades.
Do not distribute the installer until the manual checks below pass.

To publish or inspect the application without compiling Setup:

```powershell
dotnet publish WinSearch.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile=Setup-win-x64
.\scripts\Test-Publish.ps1
```

Distribute the complete publish directory, never just `WinSearch.exe`. The app is
folder-based and self-contained for both .NET and Windows App SDK. Trimming is
disabled to preserve WinRT/XAML/COM behavior. ReadyToRun is disabled in the Setup
profile. The installer recursively includes resources, languages and native DLLs.
The icon is explicitly copied because the window loads it from `AppContext.BaseDirectory`.

## Package identity and compatibility audit

| Area | Source assessment / required verification |
| --- | --- |
| Launcher layout | `MainWindow.xaml` and window code are unchanged. Check theme, DPI, position and result resizing. |
| Alt+Space | Uses Win32 `RegisterHotKey` and a window subclass, not package activation. Check hide/show, focus loss, and reopening with an empty query. |
| App/file search | Start menu scanning, user-folder indexing, shell launching and temporary icon cache do not use package identity. Check real results and launches. |
| Packaged-app discovery | Retains `PackageManager.FindPackagesForUserWithPackageTypes`, `Package.GetAppListEntriesAsync`, logos and `AppListEntry.LaunchAsync`. It queries other packages, not `Package.Current`. Test as a standard user; caught errors can silently omit packages, so successful startup alone is insufficient. |
| Manifest capabilities | `Package.appxmanifest` is retained as historical source but is not applied to desktop distribution. `runFullTrust` is replaced by normal desktop execution; the unused `systemAIModels` capability is not granted. Future AI/identity-dependent features require separate review. |
| Identity-only APIs | No current `Package.Current`, `ApplicationData.Current`, MSIX startup task, package notification registration or package-relative app-file lookup was found in authored code. Adding such APIs later may break unpackaged execution. |
| Runtime servicing | Runtimes are bundled. Publish a new installer for runtime/security updates; no automatic updater is included. |
| Architecture / OS | This installer supports native Windows 11 x64 only. ARM64 and Windows 10 are deliberately not advertised or accepted by Setup; existing source targets alone do not establish tested support. |

Microsoft documents [unpackaged WinUI distribution](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app),
[self-contained runtime deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps),
and the existing [package enumeration / launch pattern](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.core.applistentry.appusermodelid).
The self-contained setting is not a blanket guarantee for future APIs that depend
on additional runtime packages, package identity, or external components.

## Migration and signing

Close the old MSIX copy and uninstall it through Settings > Apps > Installed apps
before using Setup. Setup intentionally does not delete the old package, its data,
or trusted certificates. The two editions have different identities and do not
upgrade each other. Running both can cause duplicate results and Alt+Space conflicts.
The source currently stores only a disposable icon cache in the temporary folder;
there is no authored settings database to migrate. Do not remove a shared runtime.

Setup does not configure automatic startup. Launch WinSearch from Start after
sign-in. Close it through Task Manager if the panel is hidden before an upgrade or
uninstall. Inno uses Restart Manager for files in use; it does not force-kill apps.

An unsigned Setup.exe may still trigger SmartScreen or organizational policy.
Switching from MSIX removes certificate-import requirements, not publisher-trust
requirements. For public distribution, Authenticode-sign the published application
before compiling Setup and sign/timestamp Setup afterward using a trusted signing
service or certificate. Configure Inno's signed uninstaller support if required.
Never commit private keys. Recompute the SHA-256 file after signing.

## Manual acceptance checklist (required before public release)

Use a clean Windows 11 x64 VM as a standard user with no development certificate,
.NET 10 runtime, or Windows App Runtime installed. Record Windows build and results.

1. Install offline. Confirm no certificate/runtime dependency prompts, correct
   per-user destination, Start shortcut, optional desktop shortcut and Apps entry.
2. Start from each shortcut. Confirm the app stays running and displays its icon,
   layout and light/dark themes at 100%, 150% and 200% DPI.
3. From another app, press Alt+Space repeatedly. Verify focus, typing, hide/show,
   dismiss-on-focus-loss and query clearing. Ensure the old MSIX is not running.
4. Search and launch a Start menu desktop application. Search and launch a packaged
   application such as Calculator; verify its name and logo. Check one without a
   conventional `.lnk` shortcut so Start menu scanning cannot mask a discovery failure.
5. Add a harmless uniquely named text file and folder in Documents before starting
   WinSearch. Wait for indexing, search each name, open with Enter, verify file
   association and folder opening. Check Escape and maximum-three-result layout.
6. Re-run Setup over the same version, then test a higher `-Version`. Check in-use
   handling, unchanged install location, one Apps entry and working shortcuts.
7. Uninstall both while stopped and while running. Confirm installed files and
   shortcuts are removed, no unrelated files are removed, and reinstall works.
8. Verify startup after reboot by launching from Start. Repeat on a second clean VM.

`Test-Publish.ps1` checks payload structure and architecture only. A successful
publish or installer compile is not an installation or functionality test.

## Validation recorded on 2026-11-01

- .NET SDK 10.0.403: Release win-x64 publish succeeded using the Setup profile.
- Payload validation passed: x64 executable, .NET runtime, Windows App SDK DLL,
  WinUI DLL, PRI and icon, with no MSIX/certificate artifacts.
- Inno Setup 6.7.3 compiled `WinSearch-Setup-1.0.0.exe` successfully. The installer
  is unsigned and has not been installed on a test machine.
- UI automation was interrupted by the user with Escape. A window named Win Search
  appeared, but its returned identifier matched the older packaged identity; the
  unpackaged process and interactions were not verified. Do not count this as a
  successful runtime smoke test.
- Clean-machine installation, upgrade/uninstall, global hotkey, search, packaged
  discovery/launch, DPI and offline runtime tests have **not been run**.

The existing MSIX prerelease remains available. Keep the Setup build a test candidate
until the above acceptance checks pass.
