using System.Text;

namespace Menees.Chords.Db;

/// <summary>Immutable book-wide suggestions built only from catalog metadata, including archived songs.</summary>
public sealed class BookMetadataValueIndex
{
	private readonly Dictionary<string, IReadOnlyList<string>> values = new(StringComparer.Ordinal);

	/// <summary>Builds distinct, normalized suggestions for artist, tag, album and time signature.</summary>
	public BookMetadataValueIndex(ChordDatabase database)
	{
		ArgumentNullException.ThrowIfNull(database);
		Dictionary<string, HashSet<string>> unique = new(StringComparer.Ordinal)
		{
			["artist"] = new(StringComparer.OrdinalIgnoreCase),
			["tag"] = new(StringComparer.OrdinalIgnoreCase),
			["album"] = new(StringComparer.OrdinalIgnoreCase),
			["time"] = new(StringComparer.OrdinalIgnoreCase),
		};
		foreach (Song song in database.Songs)
		{
			Add(unique["artist"], song.Artists);
			Add(unique["tag"], song.Tags);
			Add(unique["album"], SongMetadata.GetValues(song, "album"));
			Add(unique["time"], SongMetadata.GetValues(song, "time"));
		}

		foreach ((string name, HashSet<string> choices) in unique)
		{
			this.values.Add(name, Array.AsReadOnly(choices.Order(StringComparer.OrdinalIgnoreCase).ToArray()));
		}
	}

	/// <summary>Gets a detached, sorted read-only list for one supported field.</summary>
	public IReadOnlyList<string> GetValues(string field)
		=> this.values.TryGetValue(field, out IReadOnlyList<string>? choices)
			? choices : throw new ArgumentException("Choose artist, tag, album or time.", nameof(field));

	private static void Add(HashSet<string> target, IEnumerable<string> source)
	{
		foreach (string value in source)
		{
			string normalized = value.Trim().Normalize(NormalizationForm.FormC);
			if (normalized.Length > 0)
			{
				target.Add(normalized);
			}
		}
	}
}
