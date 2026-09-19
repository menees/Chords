namespace Menees.Chords.Book.Maui;

public sealed record SongPresentation(string Title, string? Html, string? PdfPath, Guid? FileId)
{
	public string? OriginalKey { get; init; }
}
