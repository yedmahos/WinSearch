# WinSearch

A Spotlight-style launcher for Windows, built with C# and WinUI. Search installed apps, files, and folders from a compact panel.

## Windows installer

WinSearch now builds as a traditional **Setup.exe** with bundled .NET and Windows App SDK runtimes. It installs for the current user on **Windows 11 x64**, with a Start menu shortcut, optional desktop shortcut and uninstall support. No MSIX certificate import is needed.

The Setup build is a **test candidate**, not a verified public release. See [build instructions, compatibility notes and the manual test checklist](docs/SETUP.md). Check [GitHub Releases](https://github.com/yedmahos/WinSearch/releases) for available assets; source archives are not installers.

When a tested Setup asset is published, download `WinSearch-Setup-<version>.exe`, run it, then launch WinSearch from Start. An unsigned installer may still display SmartScreen warnings.

### Moving from the older MSIX release

Close and uninstall the old MSIX edition through **Settings > Apps > Installed apps** before running the new edition. Setup does not remove the old package or certificates. Running both editions can interfere with Alt+Space.

The [legacy v1.0.0 MSIX prerelease](https://github.com/yedmahos/WinSearch/releases/tag/v1.0.0) remains available and still requires its original self-signed certificate and dependencies. It is separate from the new Setup distribution.

## Usage

Type part of an app, file, or folder name. Results update as you type, with a maximum of three matches.

| Action | Shortcut or interaction |
| --- | --- |
| Show or hide the search panel | **Alt+Space** |
| Open the top result | **Enter** |
| Clear the current query | **Escape** |
| Dismiss the panel | Click outside it or switch to another window |

The current implementation opens the top result with Enter; result rows do not have click handlers or arrow-key selection.

Hiding the panel keeps WinSearch running and clears the query for the next search. Launch WinSearch before using the global shortcut; automatic startup is not configured by the current source. If Alt+Space does not respond, check whether another application already uses that shortcut.

## Features

- **Application search:** discovers shortcuts in the user and shared Start menus, plus packaged applications available to the current user.
- **File and folder search:** scans Desktop, Documents, Downloads, Pictures, Music, and Videos, plus OneDrive Desktop and Documents when available.
- **Name-based matching:** case-insensitive search, with exact and prefix matches ranked ahead of other substring matches within each result type.
- **Application-first results:** apps take priority, followed by files and then folders, with a distinct **Top Hit**.
- **Direct launching:** opens apps and folders, and opens files through their associated Windows applications.
- **Compact interface:** adapts to light and dark themes, resizes to its results, and displays cached app and file icons.

### Search scope and limits

Search matches names rather than file contents. File discovery runs in the background at launch, so file results may take time to appear. The scan is limited to 30,000 items and a recursion depth of seven below each search root; it does not index every drive.

The scanner skips inaccessible directories, reparse-point directories, and common development folders such as `node_modules`, `.git`, `.vs`, `bin`, `obj`, and `__pycache__`. The current implementation has no live filesystem watcher; restart WinSearch to refresh discovery after adding or removing items.

## Source and development

The project uses **C#**, **WinUI**, **.NET 10**, and the **Windows App SDK**, with unpackaged, self-contained desktop distribution.

- [`WinSearch.csproj`](WinSearch.csproj): target framework, dependencies, and build settings.
- [`MainWindow.xaml`](MainWindow.xaml): search panel layout.
- [`MainWindow.xaml.cs`](MainWindow.xaml.cs): discovery, ranking, results, icons, and launching.
- [`MainWindow.Hotkey.cs`](MainWindow.Hotkey.cs): global shortcut and panel visibility.
- [`installer/WinSearch.iss`](installer/WinSearch.iss): Windows installer definition.
- [`docs/SETUP.md`](docs/SETUP.md): build, migration, compatibility and validation instructions.

Use a Windows development environment with .NET 10 and WinUI tooling, open `WinSearch.slnx`, restore dependencies, select x64 and use the **WinSearch (Unpackaged)** launch profile. Build Setup with `scripts/Build-Setup.ps1` as described in [SETUP.md](docs/SETUP.md). Only native Windows 11 x64 is targeted by the installer.

## Feedback

[Report an issue](https://github.com/yedmahos/WinSearch/issues) with your Windows version, the WinSearch release, steps to reproduce the problem, and any installation error text.
