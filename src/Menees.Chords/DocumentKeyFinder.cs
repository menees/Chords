namespace Menees.Chords;

#region Using Directives

using System.Diagnostics.CodeAnalysis;
using Menees.Chords.Parsers;

#endregion

internal static class DocumentKeyFinder
{
	#region Internal Methods

	internal static Key? Find(Document document, DetectKey detectKey)
	{
		Key? result = FindMetadataKey(document.Entries);
		if (result is null && detectKey != DetectKey.MetadataOnly)
		{
			IEnumerable<Chord> chords = GetChords(document.Entries).Where(chord => chord.Notation == Notation.Name);
			Chord? chord = detectKey == DetectKey.FirstChord ? chords.FirstOrDefault() : chords.LastOrDefault();
			if (chord is not null)
			{
				result = Key.FromChord(chord);
			}
		}

		return result;
	}

	internal static bool TryGetMetadata(Entry entry, [NotNullWhen(true)] out MetadataEntry? metadata)
	{
		metadata = entry switch
		{
			MetadataEntry value => value,
			ChordProDirectiveLine directive => MetadataEntry.TryParse(directive),
			_ => null,
		};
		return metadata is not null;
	}

	#endregion

	#region Private Methods

	private static Key? FindMetadataKey(IReadOnlyList<Entry> entries)
	{
		Key? result = null;
		foreach (Entry entry in entries)
		{
			IEnumerable<MetadataEntry> metadata = entry is TitleLine title ? title.Metadata : [];
			if (TryGetMetadata(entry, out MetadataEntry? value))
			{
				metadata = [value];
			}

			MetadataEntry? keyMetadata = metadata.FirstOrDefault(IsKey);
			if (keyMetadata is not null)
			{
				result = Key.Parse(keyMetadata.Argument);
				break;
			}

			if (entry is IEntryContainer container)
			{
				result = FindMetadataKey(container.Entries);
				if (result is not null)
				{
					break;
				}
			}
		}

		return result;
	}

	private static IEnumerable<Chord> GetChords(IReadOnlyList<Entry> entries)
	{
		foreach (Entry entry in entries)
		{
			if (entry is SegmentedEntry segmented)
			{
				foreach (Chord chord in segmented.Segments.OfType<ChordSegment>().Select(segment => segment.Chord))
				{
					yield return chord;
				}
			}
			else if (entry is IEntryContainer container)
			{
				foreach (Chord chord in GetChords(container.Entries))
				{
					yield return chord;
				}
			}
		}
	}

	private static bool IsKey(MetadataEntry metadata)
		=> metadata.Name.Equals("key", ChordParser.Comparison);

	#endregion
}
