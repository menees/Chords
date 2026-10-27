namespace Menees.Chords.Db;

/// <summary>One catalog group with ordered song identities.</summary>
/// <param name="Name">The group caption.</param>
/// <param name="SongIds">The filtered song identities, once per group.</param>
public sealed record BookSongGroup(string Name, IReadOnlyList<Guid> SongIds);