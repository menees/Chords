namespace Menees.Chords.Book.Maui;

internal readonly record struct CatalogCellContent(CatalogColumn Field, string Text, bool Secondary = false, bool Archived = false);
