namespace Menees.Chords.Book.Application;

/// <summary>Describes one occurrence of a song in an ordered setlist.</summary>
public sealed record SetlistEntryCatalogItem(
	Guid EntryId,
	SongCatalogItem Song,
	string Details)
{
	public Guid SongId => this.Song.Id;

	public string DisplayText => this.Details.Length == 0 ? this.Song.DisplayText : $"{this.Song.DisplayText} · {this.Details}";
}
