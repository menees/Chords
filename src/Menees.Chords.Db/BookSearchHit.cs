namespace Menees.Chords.Db;

/// <summary>Represents a lightweight search result.</summary>
public sealed record BookSearchHit(Guid SongId, string Title, IReadOnlyList<string> Artists);
