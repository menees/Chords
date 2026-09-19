using Menees.Chords.Db;

namespace Menees.Chords.Sync;

public sealed record BookMergeResult(ChordDatabase Database, IReadOnlyList<SyncConflict> Conflicts, IReadOnlyList<SyncRecoverySource> Recoveries);
