using System.Text;

namespace Menees.Chords.Db;

/// <summary>Indexes catalog and earlier incoming identities without opening unrelated assets.</summary>
internal sealed class BookImportMatcher
{
	private readonly Dictionary<string, List<BookImportCandidate>> hashes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, List<BookImportCandidate>> titles = new(StringComparer.Ordinal);

	public BookImportMatcher(ChordDatabase database)
	{
		Dictionary<Guid, Song> songs = database.Songs.ToDictionary(song => song.Id);
		HashSet<Guid> withSheets = [];
		foreach (SongFile file in database.SongFiles)
		{
			Song song = songs[file.SongId];
			withSheets.Add(song.Id);
			this.Add(new(song.Id, file.Id, song.Title, song.Artists.AsReadOnly(), file.RelativePath, song.IsArchived, false, file.ContentHash));
		}

		foreach (Song song in database.Songs.Where(song => !withSheets.Contains(song.Id)))
		{
			this.Add(new(song.Id, Guid.Empty, song.Title, song.Artists.AsReadOnly(), "No sheets", song.IsArchived, false, string.Empty));
		}
	}

	public BookImportItem Match(Guid id, string name, string hash, SongFileAnalysis analysis)
	{
		List<BookImportCandidate> content = this.hashes.GetValueOrDefault(hash) ?? [];
		IEnumerable<BookImportCandidate> versions = (this.titles.GetValueOrDefault(Normalize(analysis.Title)) ?? [])
			.Where(candidate => RelatedArtists(candidate.Artists, analysis.Artists));
		BookImportCandidate[] matches = [.. content.Concat(versions).DistinctBy(candidate => (candidate.SongId, candidate.SheetId))];
		BookImportConflict conflict = content.Any(candidate => Normalize(candidate.Title) == Normalize(analysis.Title))
			? BookImportConflict.ExactDuplicate : content.Count > 0 ? BookImportConflict.IdenticalContent
			: matches.Length > 0 ? BookImportConflict.PossibleVersion : BookImportConflict.NewSong;
		BookImportItem result = new(id, name, hash, analysis, conflict, Array.AsReadOnly(matches));
		this.Add(new(id, id, analysis.Title, analysis.Artists, name, false, true, hash));
		return result;
	}

	private static bool RelatedArtists(IReadOnlyList<string> left, IReadOnlyList<string> right)
	{
		HashSet<string> first = left.Select(Normalize).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
		HashSet<string> second = right.Select(Normalize).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
		return first.Count == 0 || second.Count == 0 || first.SetEquals(second);
	}

	private static string Normalize(string text) => text.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();

	private static void Add(Dictionary<string, List<BookImportCandidate>> index, string key, BookImportCandidate candidate)
	{
		if (!index.TryGetValue(key, out List<BookImportCandidate>? matches))
		{
			matches = [];
			index.Add(key, matches);
		}

		matches.Add(candidate);
	}

	private void Add(BookImportCandidate candidate)
	{
		if (candidate.ContentHash.Length > 0)
		{
			Add(this.hashes, candidate.ContentHash, candidate);
		}

		Add(this.titles, Normalize(candidate.Title), candidate);
	}
}