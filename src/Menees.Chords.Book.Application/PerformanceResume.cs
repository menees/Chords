namespace Menees.Chords.Book.Application;

/// <summary>A recovered navigation sequence containing only currently available songs.</summary>
public sealed record PerformanceResume(Guid? SetlistId, IReadOnlyList<Guid> SongIds, IReadOnlyList<Guid> EntryIds, string? Name, int Index);
