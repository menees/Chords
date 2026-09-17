# Offline metronome samples

These five original synthetic percussion samples are distributed under the
repository license. No third-party recordings or downloads are required.
Each file contains 4,800 mono samples at 48 kHz, encoded as little-endian IEEE
float32 without a header (19,200 bytes each). They are embedded in the application
library and preloaded/resampled before the Windows audio clock starts.

Regenerate with `eng/tests/New-MetronomeSamples.ps1`. All sounds use the same
100 ms analysis window and target RMS of 0.10 with peak limiting at 0.70, leaving
headroom for the 1.3x single-sound first-beat accent. The default pattern selects
kick alone on the accented first beat and hi-hat alone otherwise. When accenting
is disabled, the default uses only hi-hat. Legacy `Click` settings still load.
Perceived loudness and timbre require listening acceptance on real output routes.

Tempo counts the selected beat unit; subdivision is clicks per beat (1–4,
including triplets). The renderer derives each deadline from the absolute sample
position, skips expired clicks after discontinuities, and allocates nothing while
rendering. It does not synthesize oscillators or decode samples in audio callbacks.

Run `eng/tests/Test-NativeMetronome.ps1` on Windows for the muted native buffer,
startup/cancellation and visual-only checks. These tests do not establish audible
latency, route-change recovery or long-session Surface timing quality.
