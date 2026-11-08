using System.Globalization;

namespace Menees.Chords.Db;

/// <summary>Resolves catalog scalar overrides without discarding the original source observations.</summary>
public static class SongMetadata
{
	private const int MaximumCapoFret = 24;

	/// <summary>Gets scalar values, preferring an explicit catalog value (including an explicitly empty value).</summary>
	public static IReadOnlyList<string> GetValues(Song song, string name)
	{
		ArgumentNullException.ThrowIfNull(song);
		IReadOnlyList<string> result = song.MetadataOverrides.TryGetValue(name, out List<string>? values) ? values
			: song.SourceMetadata.TryGetValue(name, out List<SourceMetadataValue>? source) ? [.. source.Select(value => value.Value)] : [];
		return FilterValues(name, result);
	}

	/// <summary>Enumerates effective scalar values once per field.</summary>
	public static IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> Enumerate(Song song)
	{
		ArgumentNullException.ThrowIfNull(song);
		foreach ((string name, List<SourceMetadataValue> source) in song.SourceMetadata)
		{
			IReadOnlyList<string> values = song.MetadataOverrides.TryGetValue(name, out List<string>? overrides)
				? overrides : [.. source.Select(value => value.Value)];
			yield return new(name, FilterValues(name, values));
		}

		foreach ((string name, List<string> values) in song.MetadataOverrides)
		{
			if (!song.SourceMetadata.ContainsKey(name))
			{
				yield return new(name, FilterValues(name, values));
			}
		}
	}

	/// <summary>Parses a catalog capo fret from 1 through 24.</summary>
	public static bool TryParseCapo(string text, out int fret)
		=> int.TryParse(text.AsSpan().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out fret) && fret is >= 1 and <= MaximumCapoFret;

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

	private static IReadOnlyList<string> FilterValues(string name, IReadOnlyList<string> values)
		=> name.Equals("capo", StringComparison.OrdinalIgnoreCase) && values.Any(value => !TryParseCapo(value, out _))
			? [.. values.Where(value => TryParseCapo(value, out _))] : values;
}
