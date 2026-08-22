namespace Menees.Chords.Transformers;

#region Using Directives

using System.Diagnostics.CodeAnalysis;
using Menees.Chords.Parsers;

#endregion

internal sealed class ChordDocumentTransformer
{
	#region Private Data Members

	private readonly Func<Chord, Key, Chord> changeChord;
	private readonly Func<Key, Key> changeKey;
	private Key currentKey;

	#endregion

	#region Constructors

	internal ChordDocumentTransformer(Key key, Func<Chord, Key, Chord> changeChord, Func<Key, Key>? changeKey = null)
	{
		this.currentKey = key;
		this.changeChord = changeChord;
		this.changeKey = changeKey ?? (value => value);
	}

	#endregion

	#region Internal Methods

	internal IReadOnlyList<Entry> Transform(IReadOnlyList<Entry> entries)
	{
		List<Entry>? output = null;
		for (int index = 0; index < entries.Count; index++)
		{
			Entry original = entries[index];
			Entry transformed = this.Transform(original);
			if (!ReferenceEquals(original, transformed))
			{
				output ??= [.. entries.Take(index)];
			}

			output?.Add(transformed);
		}

		return output ?? entries;
	}

	#endregion

	#region Private Methods

	private Entry Transform(Entry entry)
	{
		Entry result;
		if (this.TryTransformKeyMetadata(entry, out Entry? metadataResult))
		{
			result = metadataResult;
		}
		else if (entry is ChordProDirectiveLine directive && this.TryTransformChordDirective(directive, out ChordProDirectiveLine? directiveResult))
		{
			result = directiveResult;
		}
		else
		{
			result = entry switch
			{
				ChordLine line => this.Transform(line),
				ChordProLyricLine line => this.Transform(line),
				ChordProGridLine line => this.Transform(line),
				ChordLyricPair pair => this.Transform(pair),
				ChordDefinitions definitions => this.Transform(definitions),
				Section section => this.Transform(section),
				TitleLine title => this.Transform(title),
				_ => entry,
			};
		}

		IReadOnlyList<Entry> annotations = this.Transform(entry.Annotations);
		if (!ReferenceEquals(annotations, entry.Annotations))
		{
			result = result.Clone(annotations);
		}

		return result;
	}

	private ChordDefinitions Transform(ChordDefinitions definitions)
	{
		IReadOnlyList<ChordDefinition> changed = [.. definitions.Definitions.Select(
			definition => definition.ChangeChord(chord => this.changeChord(chord, this.currentKey)))];
		return changed.SequenceEqual(definitions.Definitions) ? definitions : new ChordDefinitions(changed);
	}

	private Entry Transform(ChordLine line)
		=> this.TransformSegments(line, segments => new ChordLine(segments));

	private Entry Transform(ChordProGridLine line)
		=> this.TransformSegments(line, segments => new ChordProGridLine(segments));

	private Entry Transform(ChordProLyricLine line)
		=> this.TransformSegments(line, segments => new ChordProLyricLine(segments));

	private ChordLyricPair Transform(ChordLyricPair pair)
	{
		ChordLine chords = (ChordLine)this.Transform(pair.Chords);
		LyricLine lyrics = (LyricLine)this.Transform(pair.Lyrics);
		return ReferenceEquals(chords, pair.Chords) && ReferenceEquals(lyrics, pair.Lyrics)
			? pair : new ChordLyricPair(chords, lyrics);
	}

	private Section Transform(Section section)
	{
		IReadOnlyList<Entry> entries = this.Transform(section.Entries);
		return ReferenceEquals(entries, section.Entries) ? section : new Section(entries);
	}

	private TitleLine Transform(TitleLine title)
		=> title.ChangeMetadata(this.Transform);

	private MetadataEntry Transform(MetadataEntry metadata)
	{
		MetadataEntry result = metadata;
		if (metadata.Name.Equals("key", ChordParser.Comparison))
		{
			Key key = Key.Parse(metadata.Argument);
			this.currentKey = key;
			Key changed = this.changeKey(key);
			if (!changed.Equals(key))
			{
				result = new(metadata.Name, changed.Name);
			}
		}

		return result;
	}

	private Entry TransformSegments(SegmentedEntry line, Func<IReadOnlyList<TextSegment>, Entry> create)
	{
		IReadOnlyList<TextSegment> segments = [.. line.Segments.Select(segment => segment is ChordSegment chord
			? chord.ChangeChord(value => this.changeChord(value, this.currentKey)) : segment)];
		return segments.SequenceEqual(line.Segments) ? line : create(segments);
	}

	private bool TryTransformKeyMetadata(Entry entry, [NotNullWhen(true)] out Entry? result)
	{
		result = null;
		if (DocumentKeyFinder.TryGetMetadata(entry, out MetadataEntry? metadata)
			&& metadata.Name.Equals("key", ChordParser.Comparison))
		{
			Key key = Key.Parse(metadata.Argument);
			this.currentKey = key;
			Key changed = this.changeKey(key);
			if (entry is MetadataEntry)
			{
				result = changed.Equals(key) ? entry : new MetadataEntry(metadata.Name, changed.Name);
			}
			else
			{
				ChordProDirectiveLine directive = (ChordProDirectiveLine)entry;
				string argument = directive.LongName.Equals("meta", ChordParser.Comparison)
					? $"key {changed.Name}" : changed.Name;
				result = changed.Equals(key) ? entry : ChordProDirectiveLine.Create(directive.QualifiedName, argument);
			}
		}

		return result is not null;
	}

	private bool TryTransformChordDirective(
		ChordProDirectiveLine directive,
		[NotNullWhen(true)] out ChordProDirectiveLine? result)
	{
		result = null;
		string longName = directive.LongName;
		if ((longName.Equals("chord", ChordParser.Comparison) || longName.Equals("define", ChordParser.Comparison))
			&& !string.IsNullOrEmpty(directive.Argument))
		{
			string argument = directive.Argument!;
			int separator = argument.IndexOfAny([' ', '\t']);
			string chordText = separator < 0 ? argument : argument.Substring(0, separator);
			if (Chord.TryParse(chordText, out Chord? chord))
			{
				Chord changed = this.changeChord(chord, this.currentKey);
				string changedArgument = changed.Name + (separator < 0 ? string.Empty : argument.Substring(separator));
				result = ReferenceEquals(changed, chord) ? directive
					: ChordProDirectiveLine.Create(directive.QualifiedName, changedArgument);
			}
		}

		return result is not null;
	}

	#endregion
}
