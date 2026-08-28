namespace Menees.Chords.Db;

/// <summary>Identifies the song and managed file created by an import.</summary>
public sealed record BookImportResult(Guid SongId, Guid SongFileId, string RelativePath, SongFileAnalysis Analysis);
