namespace Menees.Chords.Db;

/// <summary>Describes a newly written native book.</summary>
/// <param name="Location">The opaque store location.</param>
/// <param name="Database">The committed database, including its store-assigned identity.</param>
public sealed record NativeBookWriteResult(BookLocation Location, ChordDatabase Database);
