namespace Menees.Chords.Book.Application;

/// <summary>A saved search view supported by the current browser UI.</summary>
public sealed record CustomTabCatalogItem(Guid Id, string Name, string Search, string? GroupBy, bool IsSupported);
