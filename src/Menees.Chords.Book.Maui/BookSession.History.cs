using System.Text.Json;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;

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

	public DateTimeOffset? GetSongLastAccessed(Guid songId)
	{
		this.EnsureRecentHistory();
		return this.history.AccessedUtc.TryGetValue(songId, out DateTimeOffset localAccess) ? localAccess : null;
	}

	public void FlushRecentHistory()
	{
		if (this.historyBookId is Guid id && this.history.IsDirty)
		{
			if (this.Database?.Id == id)
			{
				this.history.PruneAccessTimes(this.Database.Songs.Select(song => song.Id));
			}

			Preferences.Default.Set("ChordBook.SongAccessTimes." + id.ToString("D"), JsonSerializer.Serialize(this.history.AccessedUtc));
			Preferences.Default.Set("ChordBook.RecentSongs." + id.ToString("D"), JsonSerializer.Serialize(this.history.SongIds));
			this.history.MarkSaved();
		}
	}

	private void RecordImportedSongs(Guid bookId, IEnumerable<Guid> songIds)
	{
		if (this.Database?.Id == bookId)
		{
			this.EnsureRecentHistory();
			this.history.Record(songIds);
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
			Dictionary<Guid, DateTimeOffset>? accessed = null;
			if (id is Guid bookId)
			{
				string? json = Preferences.Default.Get<string?>("ChordBook.RecentSongs." + bookId.ToString("D"), null);
				try
				{
					saved = json is null ? null : JsonSerializer.Deserialize<Guid[]>(json);
					string preference = "ChordBook.SongAccessTimes." + bookId.ToString("D");
					if (Preferences.Default.Get<string?>(preference, null) is string dates)
					{
						bool upgraded = false;
						JsonSerializerOptions options = new();
						options.Converters.Add(new OffsetDateTimeJsonConverter(() => upgraded = true));
						accessed = JsonSerializer.Deserialize<Dictionary<Guid, DateTimeOffset>>(dates, options);
						if (upgraded)
						{
							Preferences.Default.Set(preference, JsonSerializer.Serialize(accessed));
						}
					}
				}
				catch (JsonException)
				{
					// Corrupt local history must not prevent opening a valid book.
				}
			}

			this.history = new(saved, accessed);
		}
	}
}
