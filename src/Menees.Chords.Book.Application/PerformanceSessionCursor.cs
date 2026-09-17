namespace Menees.Chords.Book.Application;

/// <summary>Identifies an exact occurrence; the context ID rejects interrupted two-part preference writes.</summary>
public sealed record PerformanceSessionCursor(Guid ContextId, Guid SongId, Guid? EntryId);
