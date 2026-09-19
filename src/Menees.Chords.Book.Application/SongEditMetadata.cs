using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>Explicit catalog edits in the song editor, independent of its text buffer.</summary>
public sealed record SongEditMetadata(string Title, IReadOnlyList<string> Artists, IReadOnlyList<string> Tags)
{
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Scalars { get; init; }
		= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

	internal void ApplyTo(Song song, SongEditDocument original)
	{
		if (this.Title != original.Title)
		{
			song.Title = this.Title.Trim();
		}

		if (!this.Artists.SequenceEqual(original.Artists, StringComparer.Ordinal))
		{
			song.Artists = [.. this.Artists.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())];
		}

		song.Tags = [.. this.Tags.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())];
		foreach ((string name, IReadOnlyList<string> values) in this.Scalars)
		{
			IReadOnlyList<string> previous = original.Metadata.TryGetValue(name, out IReadOnlyList<string>? found) ? found : [];
			if (!values.SequenceEqual(previous, StringComparer.Ordinal))
			{
				song.MetadataOverrides[name] = [.. values];
				if (name == "duration")
				{
				song.DurationSeconds = values.Count == 1 && SongMetadata.TryParseDuration(values[0], out int seconds) ? seconds : null;
				}
			}
		}
	}
}
