namespace Menees.Chords.Sync;

/// <summary>The committed cloud catalog and its asset versions. Local application and cleanup remain separate steps.</summary>
public sealed record CloudBookPublication(string DatabaseJson, CloudReplicaItem DatabaseItem, IReadOnlyDictionary<Guid, CloudReplicaItem> Assets);
