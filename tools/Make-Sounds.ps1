#requires -Version 7
<#
.SYNOPSIS
    Synthesises the three built-in alert sounds (notify, warning, critical) as 44.1 kHz 16-bit mono WAV files in
    src/EventBlitz.App/Assets/Sounds. Pure additive synthesis with an exponential envelope: an electric two-note
    chime, a descending pair and a triple pulse, all under a second so an event storm stays bearable.
#>
param(
    [string] $RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
$rate = 44100

# One note: sine with a little second/third harmonic for bite, optional pitch glide, attack + exponential decay.
function Note([double[]] $buffer, [double] $startSec, [double] $lengthSec, [double] $fromHz, [double] $toHz, [double] $gain, [double] $decay) {
    $start = [int]($startSec * $rate)
    $count = [int]($lengthSec * $rate)
    $phase = 0.0
    for ($i = 0; $i -lt $count; $i++) {
        $t = $i / $rate
        $p = $i / [double]$count
        $hz = $fromHz + ($toHz - $fromHz) * [Math]::Min(1.0, $p * 4)   # glide settles in the first quarter
        $phase += 2 * [Math]::PI * $hz / $rate
        $attack = [Math]::Min(1.0, $t / 0.004)
        $env = $attack * [Math]::Exp(-$t * $decay)
        $sample = [Math]::Sin($phase) + 0.35 * [Math]::Sin(2 * $phase) + 0.12 * [Math]::Sin(3 * $phase)
        $index = $start + $i
        if ($index -lt $buffer.Length) { $buffer[$index] += $gain * $env * $sample }
    }
}

function WriteWav([string] $path, [double[]] $buffer) {
    $peak = ($buffer | ForEach-Object { [Math]::Abs($_) } | Measure-Object -Maximum).Maximum
    $scale = if ($peak -gt 0) { 0.85 / $peak } else { 1 }
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

# notify: a bright upward "blitz" — E5 zapping up to B5, then a soft B6 sparkle.
$b = [double[]]::new([int](0.55 * $rate))
Note $b 0.00 0.30 520 988 1.0 9
Note $b 0.09 0.42 988 1319 0.8 7
Note $b 0.18 0.35 1976 1976 0.25 12
WriteWav (Join-Path $dir 'notify.wav') $b

# warning: two descending tones with a rougher edge, like a door chime that is not happy.
$b = [double[]]::new([int](0.70 * $rate))
Note $b 0.00 0.32 1046 988 1.0 8
Note $b 0.22 0.45 740 698 1.0 6
WriteWav (Join-Path $dir 'warning.wav') $b

# critical: three short low pulses with a minor-third shimmer, unmistakable but not a siren.
$b = [double[]]::new([int](0.95 * $rate))
foreach ($start in 0.00, 0.26, 0.52) {
    Note $b $start 0.22 440 415 1.0 14
    Note $b $start 0.22 523 494 0.7 14
    Note $b ($start + 0.02) 0.18 220 220 0.6 16
}
WriteWav (Join-Path $dir 'critical.wav') $b

Write-Host "notify.wav, warning.wav and critical.wav written to $dir"
