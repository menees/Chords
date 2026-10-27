#region Using Directives

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>Catalog-only grouping with lazy per-field values and explicit per-song invalidation.</summary>
public sealed class BookGroupingIndex
{
	#region Private Data

	private readonly Dictionary<Guid, Song> songs;
	private readonly Dictionary<string, Dictionary<Guid, string[]>> values = new(StringComparer.Ordinal);

	#endregion

	#region Public API

	/// <summary>Initializes grouping without reading any song assets.</summary>
	public BookGroupingIndex(ChordDatabase database)
	{
		ArgumentNullException.ThrowIfNull(database);
		this.songs = database.Songs.ToDictionary(song => song.Id);
	}

	/// <summary>Gets supported saved-tab fields and their user-facing captions.</summary>
	public static IReadOnlyDictionary<string, string> Fields { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
	{
		["title"] = "Title", ["artist"] = "Artist", ["tag"] = "Tag", ["genre"] = "Genre", ["key"] = "Key",
		["tempo"] = "Tempo", ["year"] = "Year", ["capo"] = "Capo", ["time"] = "Time Signature",
	});

	/// <summary>Invalidates only the edited song's computed values.</summary>
	public void RefreshSong(Song song)
	{
		ArgumentNullException.ThrowIfNull(song);
		this.songs[song.Id] = song;
		foreach (Dictionary<Guid, string[]> field in this.values.Values)
		{
			field.Remove(song.Id);
		}
	}

	/// <summary>Groups a filtered, ordered selection, keeping each song once per individual value.</summary>
	public IReadOnlyList<BookSongGroup> Group(IReadOnlyList<Guid> songIds, string field)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		if (!Fields.ContainsKey(field))
		{
			throw new ArgumentException("Choose a supported catalog grouping field.", nameof(field));
		}

		if (!this.values.TryGetValue(field, out Dictionary<Guid, string[]>? cached))
		{
			cached = [];
			this.values.Add(field, cached);
		}

		Dictionary<string, List<Guid>> groups = new(StringComparer.OrdinalIgnoreCase);
		foreach (Guid id in songIds.Distinct())
		{
			if (!cached.TryGetValue(id, out string[]? entries))
			{
				Song song = this.songs[id];
				IEnumerable<string> source = field switch
				{
					"title" => [song.Title],
					"artist" => song.Artists,
					"tag" => song.Tags,
					_ => SongMetadata.GetValues(song, field),
				};
				entries = [.. source.Select(value => Normalize(value, field)).Where(value => value.Length > 0)
					.Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty(string.Empty)];
				cached.Add(id, entries);
			}

			foreach (string entry in entries)
			{
				if (!groups.TryGetValue(entry, out List<Guid>? members))
				{
					members = [];
					groups.Add(entry, members);
				}

				members.Add(id);
			}
		}

		bool numeric = field is "tempo" or "year" or "capo";
		return [.. groups.OrderBy(pair => pair.Key.Length == 0 ? 1 : 0)
			.ThenBy(pair => numeric && decimal.TryParse(pair.Key, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
				? value : decimal.MaxValue)
			.ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
			.Select(pair => new BookSongGroup(pair.Key.Length == 0 ? "Unspecified" : pair.Key, pair.Value.AsReadOnly()))];
	}

	#endregion

	#region Private Methods

	private static string Normalize(string value, string field)
	{
		string result = value.Trim().Normalize(NormalizationForm.FormC);
		if (field is "tempo" or "year" or "capo"
			&& decimal.TryParse(result, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
		{
			result = number.ToString("G29", CultureInfo.InvariantCulture);
		}

		return result;
	}
	#endregion
}