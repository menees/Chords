#region Using Directives

using System.Globalization;
using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>Provides a case- and diacritic-insensitive in-memory song search.</summary>
public sealed class BookSearchIndex
{
	#region Private Data

	private static readonly string[] SourceFields = ["key", "genre", "capo"];

	private readonly IReadOnlyList<SearchEntry> entries;
	private readonly Dictionary<Guid, SearchEntry> entriesById;

	#endregion

	#region Constructors

	/// <summary>Builds an immutable index over the database's current song metadata.</summary>
	public BookSearchIndex(ChordDatabase database)
	{
		ArgumentNullException.ThrowIfNull(database);
		ILookup<Guid, SongFile> files = database.SongFiles.ToLookup(file => file.SongId);
		this.entries =
		[
			.. database.Songs
				.Select(song => new SearchEntry(song.Id, song.Title, [.. song.Artists], CreateSearchText(song), CreateFields(song, files[song.Id])))
				.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(entry => entry.SongId),
		];
		this.entriesById = this.entries.ToDictionary(entry => entry.SongId);
	}

	#endregion

	#region Public API

	/// <summary>Refreshes one song's archive and override predicates without rebuilding unrelated search entries.</summary>
	public void RefreshSongPredicates(Song song)
	{
		ArgumentNullException.ThrowIfNull(song);
		Dictionary<string, string[]> fields = this.entriesById[song.Id].Fields;
		fields["archived"] = [song.IsArchived ? "true" : "false"];
		fields["display"] = [song.DisplayOverride?.HasValues == true ? "true" : "false"];
		fields["metronome"] = [song.MetronomeOverride is not null ? "true" : "false"];
	}

	/// <summary>Finds songs matching all free-text terms, quoted phrases, and supported field predicates.</summary>
	public IReadOnlyList<BookSearchHit> Search(string query, int maximumResults = int.MaxValue)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
		string normalized = Normalize(query ?? string.Empty);
		IReadOnlyList<(string? Field, string Value)> terms = BookSearchQuery.Parse(normalized);
		IEnumerable<SearchEntry> matches = terms.Count == 0
			? this.entries
			: this.entries.Where(entry => terms.All(term => Matches(entry, term.Field, term.Value)));
		return
		[
			.. matches.Take(maximumResults).Select(entry => new BookSearchHit(entry.SongId, entry.Title, entry.Artists)),
		];
	}

	#endregion

	#region Private Methods

	private static bool Matches(SearchEntry entry, string? field, string value)
	{
		bool result;
		if (field is null)
		{
			result = entry.Text.Contains(value, StringComparison.Ordinal);
		}
		else if (!entry.Fields.TryGetValue(field, out string[]? values) || value.Length == 0)
		{
			result = false;
		}
		else
		{
			bool exact = field is "key" or "capo" or "duration" or "archived" or "display" or "metronome"
				or "multiple" or "recovery" or "archivedfiles";
			result = values.Any(item => exact ? item == value : item.Contains(value, StringComparison.Ordinal));
		}

		return result;
	}

	private static Dictionary<string, string[]> CreateFields(Song song, IEnumerable<SongFile> files)
	{
		Dictionary<string, string[]> result = new(StringComparer.Ordinal)
		{
			["title"] = [Normalize(song.Title)],
			["artist"] = [.. song.Artists.Select(Normalize)],
			["tag"] = [.. song.Tags.Select(Normalize)],
			["duration"] = CreateDurationValues(song.DurationSeconds),
			["archived"] = [song.IsArchived ? "true" : "false"],
			["display"] = [song.DisplayOverride?.HasValues == true ? "true" : "false"],
			["metronome"] = [song.MetronomeOverride is not null ? "true" : "false"],
		};
		int activeCount = 0;
		bool recovery = false;
		bool archived = false;
		foreach (SongFile file in files)
		{
			activeCount += file.IsArchived || file.RecoveryVersion is not null ? 0 : 1;
			archived |= file.IsArchived;
			recovery |= file.RecoveryVersion is not null;
		}

		result["multiple"] = [activeCount > 1 ? "true" : "false"];
		result["recovery"] = [recovery ? "true" : "false"];
		result["archivedfiles"] = [archived ? "true" : "false"];
		foreach (string field in SourceFields)
		{
			result[field] = [.. SongMetadata.GetValues(song, field).Select(Normalize)];
		}

		return result;
	}

	private static string[] CreateDurationValues(int? duration)
	{
		const int SecondsPerMinute = 60;
		return duration is int seconds
			? [seconds.ToString(CultureInfo.InvariantCulture), FormattableString.Invariant($"{seconds / SecondsPerMinute}:{seconds % SecondsPerMinute:D2}")]
			: [];
	}

	private static string CreateSearchText(Song song)
	{
		IEnumerable<string> values =
		[
			song.Title,
			.. song.Artists,
			.. song.Tags,
			.. SongMetadata.Enumerate(song).SelectMany(pair => pair.Value),
		];
		return Normalize(string.Join(' ', values));
	}

	private static string Normalize(string value)
	{
		StringBuilder result = new(value.Length);
		foreach (char character in value.Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
			{
				result.Append(char.ToLowerInvariant(character));
			}
		}

		return result.ToString().Normalize(NormalizationForm.FormC);
	}

	#endregion

	#region Private Types

	private sealed record SearchEntry(
		Guid SongId, string Title, IReadOnlyList<string> Artists, string Text, Dictionary<string, string[]> Fields);

	#endregion
}
