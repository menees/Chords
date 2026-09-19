namespace Menees.Chords.Book.Application;

/// <summary>A selected field; the occurrence index is required when an existing directive will be replaced.</summary>
public sealed record MetadataDirectiveSelection(string Name, int? OccurrenceIndex);
