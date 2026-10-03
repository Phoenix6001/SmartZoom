<#
.SYNOPSIS
    Scans release files with VirusTotal and appends the results to the release notes.

.DESCRIPTION
    For each file: looks it up by SHA-256 and reuses an existing analysis if VirusTotal already has one,
    otherwise uploads it (through the large-file upload URL, since the installer is over the 32 MB limit of a
    plain upload) and waits for the analysis to finish. Files with the same content - the versioned installer
    and SmartZoom-setup.exe - are scanned once and reported together.

    It never fails the release. A detection, a timeout, a missing key or an API error is written into the
    notes as what it is, so the person reading the draft decides; a minor engine flagging a new, unsigned
    installer that hooks the keyboard is a common false positive, and the draft is where it gets looked at.

    The free API allows 4 requests a minute, so every call waits until 16 seconds after the previous one.
    Needs the API key in the VT_API_KEY environment variable.

.EXAMPLE
    pwsh .github/scripts/virustotal.ps1 -Path release/SmartZoom-0.5.0-setup.exe, release/SmartZoom-setup.exe -OutFile release-notes.md
#>
param(
    [Parameter(Mandatory)] [string[]] $Path,
    [Parameter(Mandatory)] [string] $OutFile,
    [int] $TimeoutMinutes = 20
)

$ErrorActionPreference = 'Stop'
$api = 'https://www.virustotal.com/api/v3'
$script:lastCall = [datetime]::MinValue

function Wait-ForRateLimit {
    $wait = 16 - ((Get-Date) - $script:lastCall).TotalSeconds
    if ($wait -gt 0) { Start-Sleep -Seconds ([math]::Ceiling($wait)) }
    $script:lastCall = Get-Date
}

function Invoke-VirusTotal([string] $Uri) {
    Wait-ForRateLimit
    try {
        Invoke-RestMethod -Uri $Uri -Headers @{ 'x-apikey' = $env:VT_API_KEY }
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { throw }
        # VirusTotal explains a refusal in the body ({"error": {"code", "message"}}); keep it, on one line.
        $why = try { $e = ($_.ErrorDetails.Message | ConvertFrom-Json).error; "$($e.code): $($e.message)" } catch { $_.Exception.Message }
        throw "$(([uri]$Uri).AbsolutePath.Split('/')[3]) lookup refused ($why)"
    }
}

# The upload goes through curl rather than Invoke-RestMethod -Form: PowerShell's multipart encoding adds a
# filename* parameter, and VirusTotal's upload endpoint answers that with 400 Bad Request.
function Send-File([string] $Uri, [string] $File) {
    Wait-ForRateLimit
    $response = & curl.exe --silent --show-error --fail-with-body --request POST --url $Uri `
        --header "x-apikey: $env:VT_API_KEY" --form "file=@$File"
    if ($LASTEXITCODE -ne 0) {
        $why = try { $e = ($response | ConvertFrom-Json).error; "$($e.code): $($e.message)" } catch { "curl exit $LASTEXITCODE" }
        throw "upload refused ($why)"
    }
    $response | ConvertFrom-Json
}

function Get-Report([string] $File) {
    $hash = (Get-FileHash $File -Algorithm SHA256).Hash.ToLowerInvariant()
    $link = "https://www.virustotal.com/gui/file/$hash"

    # Already known: VirusTotal keeps one report per content, whoever uploaded it.
    try {
        $stats = (Invoke-VirusTotal "$api/files/$hash").data.attributes.last_analysis_stats
        if ($stats -and ($stats.malicious + $stats.suspicious + $stats.undetected + $stats.harmless) -gt 0) {
            return @{ Hash = $hash; Link = $link; Stats = $stats }
        }
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
    }

    $uploadUrl = (Invoke-VirusTotal "$api/files/upload_url").data
    $analysisId = (Send-File $uploadUrl (Resolve-Path $File).Path).data.id

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ((Get-Date) -lt $deadline) {
        $analysis = (Invoke-VirusTotal "$api/analyses/$analysisId").data.attributes
        if ($analysis.status -eq 'completed') { return @{ Hash = $hash; Link = $link; Stats = $analysis.stats } }
    }
    return @{ Hash = $hash; Link = $link; Stats = $null }
}

$lines = @('', '## VirusTotal', '')
if (-not $env:VT_API_KEY) {
    $lines += 'Not scanned: no `VT_API_KEY` secret is configured.'
} else {
    $reports = @{}
    foreach ($file in $Path) {
        $name = Split-Path $file -Leaf
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        try {
            if (-not $reports.ContainsKey($hash)) { $reports[$hash] = Get-Report $file }
            $report = $reports[$hash]
            if ($report.Stats) {
                $s = $report.Stats
                $flagged = $s.malicious + $s.suspicious
                $scanned = $s.malicious + $s.suspicious + $s.undetected + $s.harmless
                $lines += "- ``$name``: **$flagged / $scanned** engines flagged it · [report]($($report.Link))"
            } else {
                $lines += "- ``$name``: analysis still running after $TimeoutMinutes minutes · [report]($($report.Link))"
            }
        } catch {
            $lines += "- ``$name``: not scanned ($($_.Exception.Message)) · [report](https://www.virustotal.com/gui/file/$hash)"
        }
        Write-Host $lines[-1]
    }
}

Add-Content -Path $OutFile -Value $lines -Encoding utf8
