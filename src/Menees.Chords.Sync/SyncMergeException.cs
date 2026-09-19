namespace Menees.Chords.Sync;

/// <summary>A comparison that cannot safely converge automatically; no replica should be mutated.</summary>
public sealed class SyncMergeException(string message) : Exception(message);
