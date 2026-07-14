param(
    [string]$SchedulePath = ".\Assets\StreamingAssets\audio_schedule.json",
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Speech
$waveFormat = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    44100,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono
)

$scheduleFullPath = Resolve-Path -LiteralPath $SchedulePath
$streamingRoot = Split-Path -Parent $scheduleFullPath
$schedule = Get-Content -LiteralPath $scheduleFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
$events = @($schedule.events)

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Rate = 1
$synth.Volume = 100

function Get-VoiceProfile {
    param([string]$Role)

    switch ($Role) {
        "amused_fan" {
            return @{
                Rate = "+24%"; Pitch = "+16%"; Volume = "x-loud"; NumericRate = 4; NumericVolume = 96
                Emphasis = "moderate"; PauseBeforeMs = 0; PauseAfterMs = 60
                PreferredVoices = @("Huihui", "Yaoyao")
            }
        }
        "tense_fan" {
            return @{
                Rate = "-12%"; Pitch = "-12%"; Volume = "loud"; NumericRate = -3; NumericVolume = 92
                Emphasis = "moderate"; PauseBeforeMs = 40; PauseAfterMs = 80
                PreferredVoices = @("Kangkang", "Huihui")
            }
        }
        "chant_fan" {
            return @{
                Rate = "+8%"; Pitch = "+4%"; Volume = "x-loud"; NumericRate = 1; NumericVolume = 100
                Emphasis = "strong"; PauseBeforeMs = 0; PauseAfterMs = 90
                PreferredVoices = @("Kangkang", "Huihui")
            }
        }
        "excited_fan" {
            return @{
                Rate = "+12%"; Pitch = "+10%"; Volume = "x-loud"; NumericRate = 2; NumericVolume = 98
                Emphasis = "moderate"; PauseBeforeMs = 0; PauseAfterMs = 70
                PreferredVoices = @("Huihui", "Yaoyao")
            }
        }
        default {
            return @{
                Rate = "+0%"; Pitch = "+0%"; Volume = "loud"; NumericRate = 0; NumericVolume = 90
                Emphasis = "none"; PauseBeforeMs = 0; PauseAfterMs = 60
                PreferredVoices = @("Huihui", "Yaoyao", "Kangkang")
            }
        }
    }
}

$voices = @($synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo })
$preferredVoice = $voices | Where-Object { $_.Culture.Name -like "zh*" } | Select-Object -First 1
if ($voices.Count -gt 0) {
    Write-Host ("Installed SAPI voices: " + (($voices | ForEach-Object { "$($_.Name) [$($_.Culture.Name)]" }) -join "; "))
}
if ($preferredVoice -ne $null) {
    Write-Host "Default Chinese voice: $($preferredVoice.Name)"
} else {
    Write-Host "No Chinese SAPI voice found; using default Windows voice."
}

function Resolve-VoiceName {
    param(
        [object[]]$PreferredNames,
        [string]$Text,
        [string]$Role
    )

    $isLatinText = ![string]::IsNullOrWhiteSpace($Text) -and $Text -match "[A-Za-z]" -and $Text -notmatch "[\u4e00-\u9fff]"
    if ($isLatinText) {
        $latinPreferences = @("Zira", "David")
        if ($Role -eq "tense_fan") {
            $latinPreferences = @("David", "Zira")
        }

        foreach ($latinName in $latinPreferences) {
            $latinMatch = $voices | Where-Object {
                $_.Culture.Name -like "en*" -and ($_.Name -eq $latinName -or $_.Name -like "*$latinName*")
            } | Select-Object -First 1
            if ($latinMatch -ne $null) {
                return $latinMatch.Name
            }
        }
    }

    foreach ($name in $PreferredNames) {
        if ([string]::IsNullOrWhiteSpace([string]$name)) {
            continue
        }

        $match = $voices | Where-Object {
            $_.Name -eq [string]$name -or $_.Name -like "*$([string]$name)*"
        } | Select-Object -First 1
        if ($match -ne $null) {
            return $match.Name
        }
    }

    if ($preferredVoice -ne $null) {
        return $preferredVoice.Name
    }

    return $null
}

function Format-TextForRole {
    param(
        [string]$Text,
        [string]$Role
    )

    $trimmed = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $trimmed
    }

    if ($Role -eq "chant_fan" -and $trimmed -notmatch "[!！。.?？]$") {
        return "$trimmed！"
    }

    if ($Role -eq "excited_fan" -and $trimmed.Length -le 8 -and $trimmed -notmatch "[!！。.?？]$") {
        return "$trimmed！"
    }

    if ($Role -eq "tense_fan" -and $trimmed -notmatch "[!！。.?？]$") {
        return "$trimmed。"
    }

    return $trimmed
}

function Apply-EventVoiceOverride {
    param(
        [hashtable]$Profile,
        [int]$EventOrdinal,
        [string]$Role,
        [string]$Text
    )

    $isChineseText = ![string]::IsNullOrWhiteSpace($Text) -and $Text -match "[\u4e00-\u9fff]"
    if ($EventOrdinal -le 2 -and $Role -eq "tense_fan" -and $isChineseText) {
        $Profile.Rate = "+16%"
        $Profile.Pitch = "-4%"
        $Profile.Volume = "x-loud"
        $Profile.NumericRate = 3
        $Profile.NumericVolume = 100
        $Profile.PauseBeforeMs = 0
        $Profile.PauseAfterMs = 45
        $Profile.Emphasis = "moderate"
    }

    return $Profile
}

$generated = 0
$skipped = 0
$eventOrdinal = 0
foreach ($ev in $events) {
    if ([string]::IsNullOrWhiteSpace($ev.audio_clip)) {
        continue
    }

    $eventOrdinal++

    $relative = $ev.audio_clip -replace "/", "\"
    $outPath = Join-Path $streamingRoot $relative
    $outDir = Split-Path -Parent $outPath
    if (!(Test-Path -LiteralPath $outDir)) {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    }

    if ((Test-Path -LiteralPath $outPath) -and !$Overwrite) {
        $file = Get-Item -LiteralPath $outPath
        if ($file.Length -gt 512) {
            $skipped++
            continue
        }
    }

    $text = [string]$ev.spoken_text
    if ([string]::IsNullOrWhiteSpace($text)) {
        $text = [string]$ev.text
    }
    if ([string]::IsNullOrWhiteSpace($text)) {
        continue
    }

    $role = [string]$ev.speaker_role
    $profile = Get-VoiceProfile -Role $role
    $renderText = Format-TextForRole -Text $text -Role $role
    $profile = Apply-EventVoiceOverride -Profile $profile -EventOrdinal $eventOrdinal -Role $role -Text $renderText
    $voiceName = Resolve-VoiceName -PreferredNames $profile.PreferredVoices -Text $renderText -Role $role
    if (![string]::IsNullOrWhiteSpace($voiceName)) {
        $synth.SelectVoice($voiceName)
    }

    $synth.Rate = [int]$profile.NumericRate
    $synth.Volume = [int]$profile.NumericVolume
    $ssmlText = [System.Security.SecurityElement]::Escape($renderText)
    $pauseBefore = ""
    $pauseAfter = ""
    if ([int]$profile.PauseBeforeMs -gt 0) {
        $pauseBefore = "<break time=""$([int]$profile.PauseBeforeMs)ms""/>"
    }
    if ([int]$profile.PauseAfterMs -gt 0) {
        $pauseAfter = "<break time=""$([int]$profile.PauseAfterMs)ms""/>"
    }
    $emphasisOpen = ""
    $emphasisClose = ""
    if (![string]::IsNullOrWhiteSpace([string]$profile.Emphasis) -and [string]$profile.Emphasis -ne "none") {
        $emphasisOpen = "<emphasis level=""$($profile.Emphasis)"">"
        $emphasisClose = "</emphasis>"
    }
    $ssml = @"
<speak version="1.0" xml:lang="zh-CN">
  $pauseBefore<prosody rate="$($profile.Rate)" pitch="$($profile.Pitch)" volume="$($profile.Volume)">$emphasisOpen$ssmlText$emphasisClose</prosody>$pauseAfter
</speak>
"@

    try {
        $synth.SetOutputToWaveFile($outPath, $waveFormat)
    } catch {
        $synth.SetOutputToWaveFile($outPath)
    }
    try {
        $synth.SpeakSsml($ssml)
    } catch {
        $synth.Speak($renderText)
    }
    $synth.SetOutputToNull()
    $generated++
    Write-Host "Generated $relative [$role/$voiceName] <= $renderText"
}

$synth.Dispose()
Write-Host "Done. Generated=$generated Skipped=$skipped"
