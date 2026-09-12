using System.Text.Json;
using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

public sealed partial class BookSession
{
	private Guid? historyBookId;
	private RecentSongHistory history = new();

	public void RecordSongAccess(Guid songId)
	{
		this.EnsureRecentHistory();
		this.history.Record(songId);
	}

	public IReadOnlyList<SongRow> SearchRecentSongs(string? query, bool includeArchived)
	{
		this.EnsureRecentHistory();
		Dictionary<Guid, SongRow> matches = this.SearchSongs(query, includeArchived).ToDictionary(song => song.Id);
		return [.. this.history.SongIds.Where(matches.ContainsKey).Select(id => matches[id])];
	}

	public void FlushRecentHistory()
	{
		if (this.historyBookId is Guid id && this.history.IsDirty)
		{
			Preferences.Default.Set("ChordBook.RecentSongs." + id.ToString("D"), JsonSerializer.Serialize(this.history.SongIds));
			this.history.MarkSaved();
		}
	}

	private void EnsureRecentHistory()
	{
		Guid? id = this.Database?.Id;
		if (id != this.historyBookId)
		{
			this.FlushRecentHistory();
			this.historyBookId = id;
			Guid[]? saved = null;
			if (id is Guid bookId)
			{
				string? json = Preferences.Default.Get<string?>("ChordBook.RecentSongs." + bookId.ToString("D"), null);
				try
				{
					saved = json is null ? null : JsonSerializer.Deserialize<Guid[]>(json);
				}
				catch (JsonException)
				{
					// Corrupt local history must not prevent opening a valid book.
				}
			}

			this.history = new(saved);
		}
	}
}
