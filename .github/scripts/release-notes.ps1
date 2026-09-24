<#
.SYNOPSIS
    Writes the CHANGELOG section for one version to a file, for use as GitHub Release notes.

.DESCRIPTION
    CHANGELOG.md follows Keep a Changelog: one "## [x.y.z] - date" heading per release. This copies the
    body of the heading for -Version (everything up to the next "## " heading), drops the leading and
    trailing blank lines, and fails if the section does not exist or is empty, so a release cannot be cut
    with nothing to say about it.

.EXAMPLE
    pwsh .github/scripts/release-notes.ps1 -Version 0.1.0 -OutFile release-notes.md
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutFile,
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '..\..\CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'

$lines = Get-Content $ChangelogPath
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^## \[$([regex]::Escape($Version))\]") { $start = $i; break }
}
if ($start -lt 0) {
    throw "CHANGELOG.md has no '## [$Version]' section. Move the Unreleased entries under one before tagging."
}

$body = New-Object System.Collections.Generic.List[string]
for ($i = $start + 1; $i -lt $lines.Count -and $lines[$i] -notmatch '^## '; $i++) {
    $body.Add($lines[$i])
}
while ($body.Count -gt 0 -and $body[0].Trim() -eq '') { $body.RemoveAt(0) }
while ($body.Count -gt 0 -and $body[$body.Count - 1].Trim() -eq '') { $body.RemoveAt($body.Count - 1) }
if ($body.Count -eq 0) {
    throw "The '## [$Version]' section of CHANGELOG.md is empty."
}

Set-Content -Path $OutFile -Value ($body -join "`n") -Encoding utf8 -NoNewline
Write-Host "Release notes for ${Version}: $($body.Count) lines -> $OutFile"
