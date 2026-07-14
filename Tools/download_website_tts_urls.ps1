param(
    [Parameter(Mandatory = $true)]
    [string]$UrlManifest,

    [string]$DownloadDir = ".\Tools\WebsiteTTSDownloads",
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"

$manifestPath = Resolve-Path -LiteralPath $UrlManifest
$downloadPath = Resolve-Path -LiteralPath $DownloadDir
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$results = @($manifest.results)

if ($results.Count -eq 0) {
    throw "No results found in $UrlManifest"
}

$downloaded = 0
$skipped = 0
$failed = 0
$log = @()

foreach ($result in $results) {
    if ([string]$result.status -ne "generated") {
        $failed++
        $log += [pscustomobject]@{
            id = [string]$result.id
            file = [string]$result.file
            status = "not_generated"
            reason = [string]$result.reason
        }
        continue
    }

    $target = Join-Path $downloadPath ([string]$result.file)
    if ((Test-Path -LiteralPath $target) -and !$Overwrite) {
        $skipped++
        $log += [pscustomobject]@{
            id = [string]$result.id
            file = [string]$result.file
            status = "skipped_exists"
        }
        continue
    }

    try {
        Invoke-WebRequest -Uri ([string]$result.src) -OutFile $target
        $item = Get-Item -LiteralPath $target
        $downloaded++
        $log += [pscustomobject]@{
            id = [string]$result.id
            file = [string]$result.file
            status = "downloaded"
            length = $item.Length
        }
        Write-Host "Downloaded $($result.file) ($($item.Length) bytes)"
    } catch {
        $failed++
        $log += [pscustomobject]@{
            id = [string]$result.id
            file = [string]$result.file
            status = "download_failed"
            error = $_.Exception.Message
        }
        Write-Warning "Failed $($result.file): $($_.Exception.Message)"
    }
}

$downloadLogPath = [System.IO.Path]::ChangeExtension($manifestPath, ".download.json")
[pscustomobject]@{
    schema_version = "website_tts_url_download_log_v1"
    source_manifest = Split-Path -Leaf $manifestPath
    downloaded = $downloaded
    skipped = $skipped
    failed = $failed
    results = $log
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $downloadLogPath -Encoding UTF8

Write-Host "Done. Downloaded=$downloaded Skipped=$skipped Failed=$failed"
