# Original synthetic percussion for ChordBook, distributed under the repository license.
# Run from any directory. Output: 48 kHz mono little-endian IEEE float PCM, no header.
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot '../../src/Menees.Chords.Book.Application/Sounds'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$rate = 48000
foreach ($name in @('Kick', 'HiHat', 'Woodblock', 'Cowbell', 'Click')) {
    $random = [Random]::new(1729)
    $values = [float[]]::new(4800)
    $energy = 0.0
    $peak = 0.0
    $previousNoise = 0.0
    for ($i = 0; $i -lt $values.Length; $i++) {
        $t = $i / [double]$rate
        $attack = [Math]::Min(1, $t / 0.001)
        $value = switch ($name) {
            'Kick' { [Math]::Sin(2 * [Math]::PI * (55 * $t + 1.5 * (1 - [Math]::Exp(-50 * $t)))) * [Math]::Exp(-55 * $t) }
            'HiHat' { $noise = 2 * $random.NextDouble() - 1; $high = $noise - $previousNoise; $previousNoise = $noise; $high * [Math]::Exp(-50 * $t) }
            'Woodblock' { ([Math]::Sin(2 * [Math]::PI * 900 * $t) + 0.5 * [Math]::Sin(2 * [Math]::PI * 1430 * $t)) * [Math]::Exp(-90 * $t) }
            'Cowbell' { ([Math]::Sin(2 * [Math]::PI * 540 * $t) + 0.7 * [Math]::Sin(2 * [Math]::PI * 800 * $t)) * [Math]::Exp(-55 * $t) }
            'Click' { [Math]::Sin(2 * [Math]::PI * 1000 * $t) * [Math]::Exp(-90 * $t) }
        }
        $values[$i] = $value * $attack * [Math]::Min(1, (0.1 - $t) / 0.01)
        $energy += $values[$i] * $values[$i]
        $peak = [Math]::Max($peak, [Math]::Abs($values[$i]))
    }
    # Match RMS over the same window, with headroom for accent gain. Listening QA still required.
    $gain = [Math]::Min(0.10 / [Math]::Sqrt($energy / $values.Length), 0.7 / $peak)
    $stream = [IO.File]::Create((Join-Path $output "$name.pcm"))
    $writer = [IO.BinaryWriter]::new($stream)
    try { foreach ($value in $values) { $writer.Write([float]($value * $gain)) } }
    finally { $writer.Dispose() }
}
