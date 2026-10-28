#region Using Directives

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>Catalog-only grouping and sorting with shared lazy values and explicit per-song invalidation.</summary>
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

		Dictionary<string, List<Guid>> groups = new(StringComparer.OrdinalIgnoreCase);
		foreach (Guid id in songIds.Distinct())
		{
			string[] entries = this.GetValues(id, field);

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

	/// <summary>Sorts distinct filtered songs by one field. Missing values stay last in either direction.</summary>
	/// <remarks>Multi-valued fields use their lowest value; ties use title, then identity, in ascending order.</remarks>
	public IReadOnlyList<Guid> Sort(IReadOnlyList<Guid> songIds, string field, bool descending = false)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		if (!Fields.ContainsKey(field))
		{
			throw new ArgumentException("Choose a supported catalog sort field.", nameof(field));
		}

		bool numeric = field is "tempo" or "year" or "capo";
		StringComparer comparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
		IEnumerable<SortEntry> entries = songIds.Distinct().Select(id =>
		{
			string[] values = this.GetValues(id, field);
			string value = values[0];
			decimal number = Number(value, numeric);
			for (int index = 1; index < values.Length; index++)
			{
				string candidate = values[index];
				decimal candidateNumber = Number(candidate, numeric);
				if (candidateNumber < number || (candidateNumber == number && comparer.Compare(candidate, value) < 0))
				{
					value = candidate;
					number = candidateNumber;
				}
			}

			return new SortEntry(id, value, number, this.GetValues(id, "title")[0]);
		});
		IOrderedEnumerable<SortEntry> ordered = entries.OrderBy(entry => entry.Value.Length == 0);
		ordered = descending
			? ordered.ThenByDescending(entry => entry.Number).ThenByDescending(entry => entry.Value, comparer)
			: ordered.ThenBy(entry => entry.Number).ThenBy(entry => entry.Value, comparer);
		return [.. ordered.ThenBy(entry => entry.Title, comparer).ThenBy(entry => entry.Id).Select(entry => entry.Id)];
	}

	#endregion

	#region Private Methods

	private static decimal Number(string value, bool numeric)
		=> numeric && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
			? number : decimal.MaxValue;

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

	private string[] GetValues(Guid id, string field)
	{
		if (!this.values.TryGetValue(field, out Dictionary<Guid, string[]>? cached))
		{
			cached = [];
			this.values.Add(field, cached);
		}

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

		return entries;
	}

	#endregion

	#region Private Types

	private readonly record struct SortEntry(Guid Id, string Value, decimal Number, string Title);

	#endregion
}