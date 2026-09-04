namespace Menees.Chords.Db;

/// <summary>Summarizes a versioned catalog-metadata refresh.</summary>
public sealed record BookMetadataRefreshResult(int AnalyzedFileCount, int UpdatedSongCount);
