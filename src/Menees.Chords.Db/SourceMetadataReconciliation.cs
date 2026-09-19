namespace Menees.Chords.Db;

/// <summary>Updates source observations while preserving catalog values that no longer match their previous source.</summary>
public static class SourceMetadataReconciliation
{
	/// <summary>Reconciles new source observations and reports independent catalog title/artist conflicts.</summary>
	public static IReadOnlyList<BookMetadataConflict> Apply(Song song, SongFileAnalysis analysis)
	{
		List<BookMetadataConflict> conflicts = [];
		string? oldTitle = Values(song, "title", "t").FirstOrDefault();
		if ((analysis.Metadata.ContainsKey("title") || analysis.Metadata.ContainsKey("t")) && song.Title != analysis.Title)
		{
			if (string.IsNullOrWhiteSpace(song.Title) || song.Title == oldTitle)
			{
				song.Title = analysis.Title;
			}
			else
			{
				conflicts.Add(new($"{song.Id:D}:title", song.Id, song.Title, "Title", song.Title, analysis.Title));
			}
		}

		string[] oldArtists = [.. Values(song, "artist", "author").Distinct(StringComparer.OrdinalIgnoreCase)];
		if (analysis.Artists.Count > 0 && !song.Artists.SequenceEqual(analysis.Artists, StringComparer.Ordinal))
		{
			if (song.Artists.SequenceEqual(oldArtists, StringComparer.Ordinal))
			{
				song.Artists = [.. analysis.Artists];
			}
			else
			{
				conflicts.Add(new(
					$"{song.Id:D}:artists", song.Id, song.Title, "Artists", string.Join("; ", song.Artists), string.Join("; ", analysis.Artists)));
			}
		}

		song.SourceMetadata.Clear();
		foreach ((string name, IReadOnlyList<SourceMetadataValue> values) in analysis.Metadata)
		{
			song.SourceMetadata[name] = [.. values.Select(value => new SourceMetadataValue { Value = value.Value, SourceName = value.SourceName })];
		}

		IReadOnlyList<string> durations = SongMetadata.GetValues(song, "duration");
		int[] parsed = [.. durations.Select(value => SongMetadata.TryParseDuration(value, out int seconds) ? (int?)seconds : null)
			.Where(value => value.HasValue).Select(value => value!.Value).Distinct()];
		if (durations.Count > 0)
		{
			song.DurationSeconds = parsed.Length == 1 && durations.All(value => SongMetadata.TryParseDuration(value, out _)) ? parsed[0] : null;
		}

		return conflicts;
	}

	internal static IEnumerable<string> Values(Song song, string first, string second)
		=> song.SourceMetadata.Where(pair => pair.Key == first || pair.Key == second).SelectMany(pair => pair.Value).Select(value => value.Value);
}
