# WinSearch

A Spotlight-style launcher for Windows. Quickly find installed apps, files, and folders from a compact search panel with light and dark themes.

## Download and install

**[Download WinSearch v1.0.2 (.exe)](https://github.com/yedmahos/WinSearch/releases/download/v1.0.2/WinSearch-Setup-1.0.2.exe)**

Requires **Windows 11 x64 (Intel/AMD)**. Windows 10 and ARM64 are not supported by this installer.

1. Download and run the installer.
2. Complete setup and launch **WinSearch** from the Start menu.
3. Press **Alt + Space** and start typing.

The installer includes the required runtimes; no separate .NET installation or MSIX certificate import is needed. It installs for your Windows account and offers an optional desktop shortcut.

[SHA-256 checksum](https://github.com/yedmahos/WinSearch/releases/download/v1.0.2/WinSearch-Setup-1.0.2.exe.sha256) · [All releases](https://github.com/yedmahos/WinSearch/releases)

**Upgrading from MSIX?** Close and uninstall the old MSIX edition through **Settings → Apps → Installed apps** first.

## Controls

| Action | Shortcut |
| --- | --- |
| Show or hide the panel | **Alt + Space** |
| Open the top result | **Enter** |
| Clear the query | **Escape** |
| Hide the panel | Click outside it |

Search displays up to **six results**. Hiding the panel keeps WinSearch running. Launch it from Start after signing in; automatic startup is not configured.

## Important notes

- Search matches names in installed apps and common user folders, not file contents or every drive. Initial file discovery may take a moment; restart WinSearch to refresh it.
- Result rows do not currently support mouse-click launching or arrow-key selection; use **Enter** for the top result.
- The installer is unsigned and may display a Windows SmartScreen warning.
- The installer’s desktop-shortcut checkbox may appear clipped at some display scales; broader installation testing is still pending.

## Development and feedback

Built with **C#, WinUI, .NET 10, and Windows App SDK**.

[Build and installer instructions](docs/SETUP.md) · [Report an issue](https://github.com/yedmahos/WinSearch/issues)
