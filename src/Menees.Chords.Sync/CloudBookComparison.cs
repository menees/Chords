using Menees.Chords.Db;

namespace Menees.Chords.Sync;

/// <summary>A read-only comparison, not an executable or confirmed transfer plan.</summary>
/// <remarks>Execution must recheck local state and conditionally replace the captured remote database version.</remarks>
public sealed record CloudBookComparison(
	CloudReplicaIdentity Target,
	CloudReplicaItem DatabaseItem,
	ChordDatabase CloudDatabase,
	BookMergeResult Merge,
	IReadOnlyDictionary<Guid, CloudReplicaItem> ManagedItems);
