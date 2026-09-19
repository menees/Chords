using System.Globalization;

namespace Menees.Chords.Db;

/// <summary>Resolves catalog scalar overrides without discarding the original source observations.</summary>
public static class SongMetadata
{
	/// <summary>Gets scalar values, preferring an explicit catalog value (including an explicitly empty value).</summary>
	public static IReadOnlyList<string> GetValues(Song song, string name)
	{
		ArgumentNullException.ThrowIfNull(song);
		return song.MetadataOverrides.TryGetValue(name, out List<string>? values) ? values
			: song.SourceMetadata.TryGetValue(name, out List<SourceMetadataValue>? source) ? [.. source.Select(value => value.Value)] : [];
	}

	/// <summary>Enumerates effective scalar values once per field.</summary>
	public static IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> Enumerate(Song song)
	{
		ArgumentNullException.ThrowIfNull(song);
		foreach ((string name, List<SourceMetadataValue> source) in song.SourceMetadata)
		{
			yield return new(name, song.MetadataOverrides.TryGetValue(name, out List<string>? values) ? values : [.. source.Select(value => value.Value)]);
		}

		foreach ((string name, List<string> values) in song.MetadataOverrides)
		{
			if (!song.SourceMetadata.ContainsKey(name))
			{
				yield return new(name, values);
			}
		}
	}

	/// <summary>Parses nonnegative whole seconds or minutes:seconds, without rewriting the source value.</summary>
	public static bool TryParseDuration(string text, out int seconds)
	{
		const int SecondsPerMinute = 60;
		string[] parts = text.Trim().Split(':');
		bool result = false;
		seconds = 0;
		if (parts.Length == 1)
		{
			result = int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds >= 0;
		}
		else if (parts.Length == 2 && parts[1].Length == 2
			&& int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
			&& int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int remainder)
			&& remainder < SecondsPerMinute && minutes <= (int.MaxValue - remainder) / SecondsPerMinute)
		{
			seconds = (minutes * SecondsPerMinute) + remainder;
			result = true;
		}

		return result;
	}
}
