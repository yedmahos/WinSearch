# WinSearch

A Spotlight-style launcher for Windows, built with C# and WinUI. Search installed apps, files, and folders from a compact panel.

## Download for Windows

**[Download WinSearch v1.0.0 — Windows x64 ZIP](https://github.com/yedmahos/WinSearch/releases/download/v1.0.0/WinSearch-v1.0.0-Windows-x64.zip)**

[View the v1.0.0 release](https://github.com/yedmahos/WinSearch/releases/tag/v1.0.0)

This release is marked **prerelease** and uses a self-signed development certificate. The installation instructions below target **Windows 11 x64**. Download the named release ZIP under **Assets**; GitHub's automatically generated source archives do not contain the installer.

## Installation

### 1. Extract the release

Extract the entire ZIP to a folder on your computer. Keep the installer, certificate, and dependency files together. The archive contains:

```text
WinSearch-1.0.0-windows-x64.msix
WinSearch.cer
INSTALL.md
Dependencies/
└── x64/
    └── Microsoft.WindowsAppRuntime.2.msix
```

### 2. Trust the signing certificate

Only trust the certificate if you trust the publisher and downloaded the files from this repository's release page. Windows does not automatically trust this self-signed certificate.

1. Open **PowerShell as Administrator**.
2. Change to the extracted folder containing `WinSearch.cer`. Replace the example path below with your actual folder.
3. Import the public certificate into **Local Machine → Trusted People**:

   ```powershell
   Set-Location -LiteralPath "C:\path\to\extracted\WinSearch"
   Import-Certificate -FilePath ".\WinSearch.cer" -CertStoreLocation "Cert:\LocalMachine\TrustedPeople"
   ```

Alternatively, double-click `WinSearch.cer`, select **Install Certificate → Local Machine**, approve the administrator prompt, then choose **Place all certificates in the following store → Browse → Trusted People** and finish the wizard. Select the machine store, not Current User.

These steps follow [Microsoft's guidance for installing a self-signed MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide#testing-distribute-to-testers-with-a-self-signed-certificate).

### 3. Install and launch

1. Double-click `WinSearch-1.0.0-windows-x64.msix` and select **Install** in Windows App Installer.
2. If Windows reports missing framework dependencies, install the matching packages from the extracted `Dependencies\x64` folder, then retry the main installer.
3. Launch **WinSearch** from the Start menu.
4. Press **Alt+Space** to show or hide the search panel while WinSearch is running.

If Windows cannot verify the publisher, confirm that the release certificate was imported into **Local Machine → Trusted People**. Do not import it into Trusted Root Certification Authorities.

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

The project uses **C#**, **WinUI**, **.NET 10**, and the **Windows App SDK**, with MSIX packaging.

- [`WinSearch.csproj`](WinSearch.csproj): target framework, dependencies, and build settings.
- [`MainWindow.xaml`](MainWindow.xaml): search panel layout.
- [`MainWindow.xaml.cs`](MainWindow.xaml.cs): discovery, ranking, results, icons, and launching.
- [`MainWindow.Hotkey.cs`](MainWindow.Hotkey.cs): global shortcut and panel visibility.
- [`Package.appxmanifest`](Package.appxmanifest): package identity and Windows capabilities.

To work on the app, use a Windows development environment with .NET 10 and WinUI/MSIX tooling, open `WinSearch.slnx` in a compatible version of Visual Studio, restore the project's NuGet packages, and select the desired build platform. The project defines x86, x64, and ARM64 targets; the published v1.0.0 download is **x64 only**.

## Feedback

[Report an issue](https://github.com/yedmahos/WinSearch/issues) with your Windows version, the WinSearch release, steps to reproduce the problem, and any installation error text.
