namespace Menees.Chords.Db;

/// <summary>A source value that differs from a catalog value edited independently of the source.</summary>
public sealed record BookMetadataConflict(string Key, Guid SongId, string SongTitle, string Field, string CatalogValue, string SourceValue);
