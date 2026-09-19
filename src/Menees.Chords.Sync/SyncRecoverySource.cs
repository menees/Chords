namespace Menees.Chords.Sync;

/// <summary>Identifies the original bytes to copy to a newly allocated archived recovery file.</summary>
public sealed record SyncRecoverySource(Guid RecoveryFileId, Guid OriginalFileId, SyncSide Side, string ContentHash);
