#region Using Directives

using System.Globalization;
using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>Provides a case- and diacritic-insensitive in-memory song search.</summary>
public sealed class BookSearchIndex
{
	#region Private Data

	private readonly IReadOnlyList<SearchEntry> entries;

	#endregion

	#region Constructors

	/// <summary>Builds an immutable index over the database's current song metadata.</summary>
	public BookSearchIndex(ChordDatabase database)
	{
		ArgumentNullException.ThrowIfNull(database);
		this.entries =
		[
			.. database.Songs
				.Select(song => new SearchEntry(song.Id, song.Title, song.Artists, CreateSearchText(song)))
				.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(entry => entry.SongId),
		];
	}

	#endregion

	#region Public API

	/// <summary>Finds songs containing every whitespace-delimited query term.</summary>
	public IReadOnlyList<BookSearchHit> Search(string query, int maximumResults = int.MaxValue)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
		string normalized = Normalize(query ?? string.Empty);
		string[] terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		IEnumerable<SearchEntry> matches = terms.Length == 0
			? this.entries
			: this.entries.Where(entry => terms.All(term => entry.Text.Contains(term, StringComparison.Ordinal)));
		return
		[
			.. matches.Take(maximumResults).Select(entry => new BookSearchHit(entry.SongId, entry.Title, entry.Artists)),
		];
	}

	#endregion

	#region Private Methods

	private static string CreateSearchText(Song song)
	{
		IEnumerable<string> values =
		[
			song.Title,
			.. song.Artists,
			.. song.Tags,
			.. song.SourceMetadata.Values.SelectMany(items => items).Select(item => item.Value),
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

	private sealed record SearchEntry(Guid SongId, string Title, IReadOnlyList<string> Artists, string Text);

	#endregion
}
