[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0',
    [string]$IsccPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $repo 'artifacts\publish\win-x64'
$output = Join-Path $repo 'artifacts\installer'

if (!$IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
    else {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6.3 or newer, or pass -IsccPath to ISCC.exe.'
}

# A fresh publish prevents stale binaries from entering the installer. Only this
# fixed, repository-owned directory is removed; no caller-supplied delete path.
if (Test-Path -LiteralPath $publish) {
    $resolved = (Resolve-Path -LiteralPath $publish).Path
    if ($resolved -ne [IO.Path]::GetFullPath($publish)) { throw 'Unexpected publish path.' }
    if ((Get-Item -LiteralPath $publish).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing to clean a linked publish directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
& dotnet publish (Join-Path $repo 'WinSearch.csproj') -c Release -r win-x64 `
    -p:Platform=x64 -p:PublishProfile=Setup-win-x64 "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

& (Join-Path $PSScriptRoot 'Test-Publish.ps1') -PublishDir $publish
& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publish" "/DSetupOutputDir=$output" `
    (Join-Path $repo 'installer\WinSearch.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
$setup = Join-Path $output "WinSearch-Setup-$Version.exe"
if (!(Test-Path -LiteralPath $setup)) { throw 'Installer output is missing.' }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath "$setup.sha256" -Encoding ascii
Write-Host "Installer: $setup"
Write-Host 'Build validation only; run the manual installation and UI checks in docs/SETUP.md.'
