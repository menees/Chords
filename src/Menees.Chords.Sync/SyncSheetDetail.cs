namespace Menees.Chords.Sync;

/// <summary>A captured, catalog-only description of one transferred sheet.</summary>
public sealed record SyncSheetDetail(Guid Id, Guid SongId, string Title, IReadOnlyList<string> Artists, string Name, string Kind);
