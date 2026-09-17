using System.Diagnostics;
using System.Reflection;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;

using WindowsMetronomeEngine engine = new();
using CancellationTokenSource cancelled = new();
cancelled.Cancel();
try
{
    await engine.StartAsync(new(), cancelled.Token);
    throw new Exception("Pre-cancelled startup succeeded.");
}
catch (OperationCanceledException) { }

// Visual-only playback must not activate an audio graph or depend on a sound device.
await engine.StartAsync(new() { AudioEnabled = false });
await Task.Delay(120);
Check(engine.IsRunning && Read("graph") is null, "Visual mode created an audio graph.");
engine.Stop();
Check(!engine.IsRunning && engine.VisualPulse is null, "Visual mode did not stop.");

foreach (string sound in new[] { "KickHiHat", "Kick", "HiHat", "Woodblock", "Cowbell", "Click" })
{
    // Exercise real native buffers without audible output on the user's machine.
    MetronomeSettings settings = new() { Sound = sound, Volume = 0, Subdivision = 3, VisualEnabled = false };
    await engine.StartAsync(settings);
    object? graph = Read("graph");
    await Task.Delay(250);
    Check(engine.IsRunning && Convert.ToInt64(Read("samplePosition")) > 0, "Audio clock did not advance.");
    Check(engine.CurrentBeat > 0 && engine.VisualPulse is null, "Audio-only mode lost beat state or produced visual pulses.");
    await engine.StartAsync(settings);
    Check(ReferenceEquals(graph, Read("graph")), "Identical settings restarted the audio graph.");
    engine.Stop();
    Check(!engine.IsRunning && Read("graph") is null, "Graph survived Stop.");
}

// Live sound/tempo edits reuse audio; visual-only mode releases audio without resetting time.
await engine.StartAsync(new() { Volume = 0 });
await Task.Delay(300);
object? liveGraph = Read("graph");
Stopwatch liveClock = (Stopwatch)Read("clock")!;
long elapsedBefore = liveClock.ElapsedTicks;
long samplesBefore = Convert.ToInt64(Read("samplePosition"));
await engine.StartAsync(new() { Volume = 0, BeatsPerMinute = 180, Sound = "Click", AccentFirstBeat = false });
Check(ReferenceEquals(liveGraph, Read("graph")), "Live sound/tempo changes reopened the audio device.");
Check(liveClock.ElapsedTicks >= elapsedBefore, "Live changes reset the beat clock.");
Check(Convert.ToInt64(Read("samplePosition")) >= samplesBefore, "Live changes reset the sample cursor.");
Check(!engine.AccentFirstBeat, "Accent change was not applied.");
await engine.StartAsync(new() { AudioEnabled = false, BeatsPerMinute = 180 });
Check(Read("graph") is null && Read("input") is null, "Disabling audio retained the native graph or input.");
Check(engine.IsRunning && liveClock.ElapsedTicks >= elapsedBefore, "Disabling audio reset the beat clock.");
long silentSamples = Convert.ToInt64(Read("samplePosition"));
double silentBeat = BeatPosition();
await Task.Delay(200);
Check(Convert.ToInt64(Read("samplePosition")) == silentSamples, "Visual-only playback still submitted audio frames.");
Check(BeatPosition() > silentBeat, "Visual beat position stopped when audio was disabled.");
double beforeEnable = BeatPosition();
await engine.StartAsync(new() { Volume = 0, BeatsPerMinute = 180 });
Check(Read("graph") is not null && !ReferenceEquals(liveGraph, Read("graph")), "Enabling audio did not create a fresh graph.");
Check(BeatPosition() >= beforeEnable && liveClock.ElapsedTicks >= elapsedBefore, "Enabling audio reset musical position.");
engine.Stop();

// Enabling audio after visual-only startup also retains the musical position.
await engine.StartAsync(new() { AudioEnabled = false, BeatsPerMinute = 60 });
await Task.Delay(350);
elapsedBefore = liveClock.ElapsedTicks;
await engine.StartAsync(new() { Volume = 0, BeatsPerMinute = 60 });
Check(Read("graph") is not null && liveClock.ElapsedTicks >= elapsedBefore, "Enabling audio reset the visual clock.");
engine.Stop();
// Stopping an asynchronous device initialization must prevent it from installing later.
Task pending = engine.StartAsync(new() { Volume = 0 });
engine.Stop();
await pending;
Check(!engine.IsRunning, "Stopped initialization later became active.");

// An older initialization must not overwrite a later visual-only start.
pending = engine.StartAsync(new() { Volume = 0 });
await engine.StartAsync(new() { AudioEnabled = false });
await pending;
Check(engine.IsRunning && Read("graph") is null, "Obsolete initialization replaced the current engine.");
engine.Stop();
Console.WriteLine("PASS: native audio buffers for six sounds, visual-only mode, identical-settings reuse, cancellation and stale-start protection.");

// Processing throughput only, not a measurement of physical playback jitter or tablet battery use.
const int Rate = 48000;
const int Seconds = 300;
float[] block = new float[Rate / 100];
MetronomeRenderer renderer = new(new() { BeatsPerMinute = 300, Subdivision = 4 }, Rate);
renderer.Render(block, 0);
Stopwatch watch = Stopwatch.StartNew();
long allocated = GC.GetAllocatedBytesForCurrentThread();
for (long position = 0; position < Rate * Seconds; position += block.Length)
{
    renderer.Render(block, position);
}
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
watch.Stop();
Check(allocated == 0, "Rendering allocated managed memory.");
Console.WriteLine($"Renderer: {Seconds} seconds at 300 BPM/4 clicks per beat processed in {watch.Elapsed.TotalMilliseconds:F1} ms; {allocated} managed bytes.");

object? Read(string name) => typeof(WindowsMetronomeEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
double BeatPosition() => (double)typeof(WindowsMetronomeEngine).GetMethod("GetBeatPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null)!;
static void Check(bool condition, string message)
{
    if (!condition) { throw new Exception(message); }
}
