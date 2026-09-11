namespace Menees.Chords.Book.Application;

/// <summary>An editor snapshot; a null text buffer indicates metadata-only editing.</summary>
public sealed record SongEditDocument(
	Guid SongId,
	long Revision,
	string Title,
	IReadOnlyList<string> Artists,
	IReadOnlyList<string> Tags,
	Guid? FileId,
	string? ContentHash,
	string? Text);
