<#
.SYNOPSIS
    Publishes SmartZoom and packages it into a per-user installer.

.DESCRIPTION
    The published executable is self-contained: it carries the .NET runtime, so the installer never has to
    ask anyone to install anything first. It is published *uncompressed* on purpose — a compressed .NET
    single file has to unpack itself on every launch, and Inno Setup compresses the payload better anyway.

    Both this script and CI use the same path, so what you build locally is what users get.

.PARAMETER Configuration
    Build configuration. Release unless you are debugging the installer itself.

.PARAMETER SkipPublish
    Package whatever is already in the publish directory. Useful when iterating on the .iss alone.

.EXAMPLE
    pwsh install\build.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $PSScriptRoot 'publish'
$script = Join-Path $PSScriptRoot 'SmartZoom.iss'

function Find-Compiler {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw "Inno Setup 6 is not installed. Get it with: winget install --id JRSoftware.InnoSetup --exact"
}

$compiler = Find-Compiler
Write-Host "Compiler: $compiler"

if (-not $SkipPublish) {
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }

    Write-Host "Publishing $Configuration, self-contained, win-x64..."
    & dotnet publish (Join-Path $root 'src\SmartZoom.App') `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained `
        -p:EnableCompressionInSingleFile=false `
        --output $payload `
        --nologo `
        --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE." }

    # The documentation XML and symbols are build outputs, not things a user needs installed.
    Get-ChildItem $payload -Include '*.pdb', '*.xml' -Recurse | Remove-Item -Force
}

$exe = Join-Path $payload 'SmartZoom.exe'
if (-not (Test-Path $exe)) { throw "No published SmartZoom.exe at $exe." }

$size = [Math]::Round((Get-Item $exe).Length / 1MB)
$version = (Get-Item $exe).VersionInfo.FileVersion
Write-Host "Payload: SmartZoom.exe $version, $size MB"

& $compiler "/DPayloadDir=$payload" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with $LASTEXITCODE." }

$setup = Get-ChildItem (Join-Path $PSScriptRoot 'output') -Filter '*-setup.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

$setupSize = [Math]::Round($setup.Length / 1MB)
Write-Host ""
Write-Host "Installer: $($setup.FullName) ($setupSize MB)"
