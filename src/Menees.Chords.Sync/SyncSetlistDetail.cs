namespace Menees.Chords.Sync;

/// <summary>A captured setlist name, independent of later catalog edits.</summary>
public sealed record SyncSetlistDetail(Guid Id, string Name);
