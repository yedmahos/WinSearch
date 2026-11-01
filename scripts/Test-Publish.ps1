[CmdletBinding()]
param([string]$PublishDir = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64'))
$ErrorActionPreference = 'Stop'
$required = @(
    'WinSearch.exe', 'WinSearch.dll', 'WinSearch.deps.json',
    'WinSearch.runtimeconfig.json', 'WinSearch.pri', 'Assets\WinSearch.ico',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
    'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll'
)
foreach ($file in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $PublishDir $file) -PathType Leaf)) {
        throw "Incomplete self-contained publish: missing $file"
    }
}
$config = Get-Content -LiteralPath (Join-Path $PublishDir 'WinSearch.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$config.runtimeOptions.includedFrameworks -or $config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) {
    throw 'The .NET runtime configuration is not self-contained.'
}
$packages = Get-ChildItem -LiteralPath $PublishDir -Recurse -File |
    Where-Object { $_.Extension -in '.msix', '.appx', '.pfx', '.cer' -or $_.Name -eq 'AppxManifest.xml' }
if ($packages) { throw 'Package/signing artifacts unexpectedly included in desktop publish.' }
$exe = [IO.File]::ReadAllBytes((Join-Path $PublishDir 'WinSearch.exe'))
$peOffset = [BitConverter]::ToInt32($exe, 0x3c)
if ([BitConverter]::ToUInt16($exe, $peOffset + 4) -ne 0x8664) {
    throw 'WinSearch.exe is not an x64 executable.'
}
Write-Host 'PASS: x64 executable, app resources, .NET and Windows App SDK payload, no MSIX/certificate artifacts.'
