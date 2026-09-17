namespace Menees.Chords.Book.Application;

/// <summary>Device-local navigation context, saved independently from the frequently changing cursor.</summary>
public sealed record PerformanceSessionContext(Guid Id, Guid BookId, Guid? SetlistId, IReadOnlyList<Guid> SongIds, string? Name);
