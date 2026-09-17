namespace Menees.Chords.Db;

/// <summary>Identifies one browser-standard key with its modifier state.</summary>
/// <param name="Key">The KeyboardEvent.key value.</param>
/// <param name="Control">Whether Control is held.</param>
/// <param name="Alt">Whether Alt is held.</param>
/// <param name="Shift">Whether Shift is held.</param>
/// <param name="Meta">Whether the platform Meta key is held.</param>
public sealed record PerformanceKeyGesture(string Key, bool Control = false, bool Alt = false, bool Shift = false, bool Meta = false);
