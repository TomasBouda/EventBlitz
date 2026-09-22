#requires -Version 7
<#
.SYNOPSIS
    Builds the three built-in alert sounds in src/EventBlitz.App/Assets/Sounds from one source recording,
    tools/notification.wav ("Notification" by original_sound, freesound.org/s/364658, CC BY 3.0):
      notify.wav   - the recording, mixed to mono and trimmed of trailing silence
      warning.wav  - the same hit pitched down (resampled) so it reads as "something is off"
      critical.wav - two hits, the second lower, unmistakable but still the same voice
    Output is 44.1 kHz 16-bit mono PCM, which is what winmm PlaySound handles without codecs.
#>
param(
    [string] $RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string] $Source = (Join-Path $PSScriptRoot 'notification.wav')
)

$ErrorActionPreference = 'Stop'
$rate = 44100

function ReadWavMono([string] $path) {
    $b = [System.IO.File]::ReadAllBytes($path)
    if ([System.Text.Encoding]::ASCII.GetString($b, 0, 4) -ne 'RIFF') { throw "$path is not a WAV file" }
    $channels = [BitConverter]::ToInt16($b, 22)
    $sampleRate = [BitConverter]::ToInt32($b, 24)
    $bits = [BitConverter]::ToInt16($b, 34)
    if ($bits -ne 16) { throw "only 16-bit PCM is handled, got $bits-bit" }
    # Find the data chunk (there may be LIST/INFO chunks in between).
    $pos = 12
    while ($pos -lt $b.Length - 8) {
        $id = [System.Text.Encoding]::ASCII.GetString($b, $pos, 4)
        $size = [BitConverter]::ToInt32($b, $pos + 4)
        if ($id -eq 'data') { break }
        $pos += 8 + $size + ($size % 2)
    }
    $dataStart = $pos + 8
    $frames = [int]($size / (2 * $channels))
    $mono = [double[]]::new($frames)
    for ($i = 0; $i -lt $frames; $i++) {
        $sum = 0.0
        for ($c = 0; $c -lt $channels; $c++) { $sum += [BitConverter]::ToInt16($b, $dataStart + ($i * $channels + $c) * 2) }
        $mono[$i] = $sum / $channels / 32768.0
    }
    if ($sampleRate -ne $rate) { $mono = Resample $mono ($sampleRate / $rate) }
    return ,$mono
}

# Linear resampling; factor > 1 shortens and raises the pitch, < 1 lengthens and lowers it.
function Resample([double[]] $samples, [double] $factor) {
    $count = [int]($samples.Length / $factor)
    $out = [double[]]::new($count)
    for ($i = 0; $i -lt $count; $i++) {
        $x = $i * $factor
        $j = [int][Math]::Floor($x)
        $frac = $x - $j
        $a = if ($j -lt $samples.Length) { $samples[$j] } else { 0 }
        $b2 = if ($j + 1 -lt $samples.Length) { $samples[$j + 1] } else { 0 }
        $out[$i] = $a + ($b2 - $a) * $frac
    }
    return ,$out
}

function Trim([double[]] $samples, [double] $threshold = 0.003, [double] $tailSec = 0.08) {
    $last = $samples.Length - 1
    while ($last -gt 0 -and [Math]::Abs($samples[$last]) -lt $threshold) { $last-- }
    $end = [Math]::Min($samples.Length, $last + [int]($tailSec * $rate))
    return ,[double[]]$samples[0..($end - 1)]
}

function Mix([double[][]] $parts, [double[]] $offsetsSec, [double[]] $gains) {
    $length = 0
    for ($p = 0; $p -lt $parts.Length; $p++) { $length = [Math]::Max($length, [int]($offsetsSec[$p] * $rate) + $parts[$p].Length) }
    $out = [double[]]::new($length)
    for ($p = 0; $p -lt $parts.Length; $p++) {
        $start = [int]($offsetsSec[$p] * $rate)
        for ($i = 0; $i -lt $parts[$p].Length; $i++) { $out[$start + $i] += $gains[$p] * $parts[$p][$i] }
    }
    return ,$out
}

function WriteWav([string] $path, [double[]] $buffer) {
    $peak = 0.0
    foreach ($v in $buffer) { if ([Math]::Abs($v) -gt $peak) { $peak = [Math]::Abs($v) } }
    $scale = if ($peak -gt 0) { 0.9 / $peak } else { 1 }
    $data = [byte[]]::new($buffer.Length * 2)
    for ($i = 0; $i -lt $buffer.Length; $i++) {
        $v = [int][Math]::Round([Math]::Clamp($buffer[$i] * $scale, -1, 1) * 32767)
        $data[2 * $i] = [byte]($v -band 0xFF)
        $data[2 * $i + 1] = [byte](($v -shr 8) -band 0xFF)
    }
    $stream = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($stream)
    $w.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int32](36 + $data.Length))
    $w.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
    $w.Write([System.Text.Encoding]::ASCII.GetBytes('fmt ')); $w.Write([int32]16)
    $w.Write([int16]1); $w.Write([int16]1); $w.Write([int32]$rate); $w.Write([int32]($rate * 2)); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([System.Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int32]$data.Length); $w.Write($data)
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
}

$dir = Join-Path $RepoRoot 'src/EventBlitz.App/Assets/Sounds'
New-Item -ItemType Directory -Force $dir | Out-Null

$hit = Trim (ReadWavMono $Source)
WriteWav (Join-Path $dir 'notify.wav') $hit
WriteWav (Join-Path $dir 'warning.wav') (Resample $hit 0.84)
$critical = Mix @((Resample $hit 0.92), (Resample $hit 0.76)) @(0.0, 0.22) @(1.0, 1.0)
WriteWav (Join-Path $dir 'critical.wav') $critical

Write-Host ("notify {0:0.00}s, warning {1:0.00}s, critical {2:0.00}s written to {3}" -f ($hit.Length / $rate), ($hit.Length / 0.84 / $rate), ($critical.Length / $rate), $dir)
