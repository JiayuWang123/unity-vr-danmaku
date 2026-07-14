param(
    [string]$PlanPath = ".\Assets\StreamingAssets\emotion_tts_website_voice_plan.json",
    [string]$DownloadDir = ".\Tools\WebsiteTTSDownloads",
    [switch]$Overwrite,
    [switch]$UseSortedFiles,
    [string]$ExpectedExtension = ".wav"
)

$ErrorActionPreference = "Stop"

function Get-NormalizedExtension {
    param([string]$Extension)

    if ([string]::IsNullOrWhiteSpace($Extension)) {
        return ".wav"
    }
    if ($Extension.StartsWith(".")) {
        return $Extension.ToLowerInvariant()
    }
    return ".$($Extension.ToLowerInvariant())"
}

function Convert-ToRelativePath {
    param(
        [string]$RootPath,
        [string]$FullPath
    )

    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\', '/')
    $full = [System.IO.Path]::GetFullPath($FullPath)
    if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length).TrimStart('\', '/')
    }
    return [System.IO.Path]::GetFileName($FullPath)
}

$expectedExt = Get-NormalizedExtension -Extension $ExpectedExtension
$planResolved = Resolve-Path -LiteralPath $PlanPath
$streamingRoot = Split-Path -Parent $planResolved
$projectRoot = Split-Path -Parent (Split-Path -Parent $streamingRoot)
$downloadResolved = Resolve-Path -LiteralPath $DownloadDir

$plan = Get-Content -LiteralPath $planResolved -Raw -Encoding UTF8 | ConvertFrom-Json
$tasks = @($plan.tasks)
if ($tasks.Count -eq 0) {
    throw "No tasks found in $PlanPath"
}

$audioExts = @(".wav", ".mp3", ".ogg")
$sortedFiles = @()
if ($UseSortedFiles) {
    $sortedFiles = @(
        Get-ChildItem -LiteralPath $downloadResolved -File |
            Where-Object { $audioExts -contains $_.Extension.ToLowerInvariant() } |
            Sort-Object LastWriteTime, Name
    )
    if ($sortedFiles.Count -lt $tasks.Count) {
        throw "Sorted import needs at least $($tasks.Count) audio files, but found $($sortedFiles.Count) in $DownloadDir"
    }
}

$results = @()
$imported = 0
$skipped = 0
$missing = 0
$extensionMismatch = 0

for ($i = 0; $i -lt $tasks.Count; $i++) {
    $task = $tasks[$i]
    $taskId = [string]$task.id
    $downloadFilename = [string]$task.download_filename
    $audioClip = [string]$task.audio_clip
    $targetRelative = $audioClip -replace "/", "\"
    $targetPath = Join-Path $streamingRoot $targetRelative
    $targetDir = Split-Path -Parent $targetPath

    $sourceFile = $null
    if ($UseSortedFiles) {
        $sourceFile = $sortedFiles[$i]
    } else {
        $exactPath = Join-Path $downloadResolved $downloadFilename
        if (Test-Path -LiteralPath $exactPath) {
            $sourceFile = Get-Item -LiteralPath $exactPath
        } else {
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($downloadFilename)
            $matches = @(
                Get-ChildItem -LiteralPath $downloadResolved -File |
                    Where-Object {
                        ($_.BaseName -eq $baseName -or $_.BaseName -like "*$taskId*") -and
                        ($audioExts -contains $_.Extension.ToLowerInvariant())
                    } |
                    Sort-Object LastWriteTime, Name
            )
            if ($matches.Count -gt 0) {
                $sourceFile = $matches[0]
            }
        }
    }

    if ($null -eq $sourceFile) {
        $missing++
        $results += [pscustomobject]@{
            id = $taskId
            status = "missing"
            expected_download_filename = $downloadFilename
            target = $targetRelative
        }
        Write-Warning "Missing download for $taskId ($downloadFilename)"
        continue
    }

    if ($sourceFile.Extension.ToLowerInvariant() -ne $expectedExt) {
        $extensionMismatch++
        $results += [pscustomobject]@{
            id = $taskId
            status = "extension_mismatch"
            source = $sourceFile.Name
            expected_extension = $expectedExt
            target = $targetRelative
        }
        Write-Warning "Skipping $($sourceFile.Name): expected $expectedExt for $taskId"
        continue
    }

    if ((Test-Path -LiteralPath $targetPath) -and !$Overwrite) {
        $skipped++
        $results += [pscustomobject]@{
            id = $taskId
            status = "skipped_exists"
            source = $sourceFile.Name
            target = $targetRelative
        }
        continue
    }

    if (!(Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    }

    Copy-Item -LiteralPath $sourceFile.FullName -Destination $targetPath -Force:$Overwrite.IsPresent
    $imported++
    $results += [pscustomobject]@{
        id = $taskId
        status = "imported"
        source = $sourceFile.Name
        target = $targetRelative
    }
    Write-Host "Imported $($sourceFile.Name) -> $targetRelative"
}

$manifestPath = Join-Path $streamingRoot "emotion_tts_website_import_manifest.json"
$manifest = [pscustomobject]@{
    schema_version = "emotion_tts_website_import_manifest_v1"
    plan = "Assets/StreamingAssets/emotion_tts_website_voice_plan.json"
    download_dir = Split-Path -Leaf $downloadResolved
    expected_extension = $expectedExt
    use_sorted_files = [bool]$UseSortedFiles
    overwrite = [bool]$Overwrite
    imported = $imported
    skipped = $skipped
    missing = $missing
    extension_mismatch = $extensionMismatch
    results = $results
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Done. Imported=$imported Skipped=$skipped Missing=$missing ExtensionMismatch=$extensionMismatch"
Write-Host "Manifest: $(Convert-ToRelativePath -RootPath $projectRoot -FullPath $manifestPath)"
