using Menees.Chords.Db;

namespace Menees.Chords.Sync;

/// <summary>A captured cloud catalog and its explicitly referenced assets.</summary>
public sealed record CloudBookSnapshot(
	string DatabaseJson,
	CloudReplicaItem DatabaseItem,
	IReadOnlyDictionary<Guid, CloudReplicaItem> ManagedItems,
	IReadOnlyDictionary<string, CloudReplicaItem> FolderItems,
	ChordDatabase Database);
