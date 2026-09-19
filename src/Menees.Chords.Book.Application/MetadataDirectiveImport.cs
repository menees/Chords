#region Using Directives

using System.Text;
using Menees.Chords.Parsers;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Previews opt-in catalog-to-directive edits. Never reads or writes book storage.</summary>
public sealed class MetadataDirectiveImport
{
	#region Implementation

	private static readonly HashSet<string> SupportedNames = new(StringComparer.Ordinal)
	{
		"title", "subtitle", "sorttitle", "artist", "album", "year", "composer", "lyricist", "copyright",
		"key", "capo", "tempo", "time", "duration", "tag", "genre",
	};

	private readonly string source;
	private readonly string newline;
	private readonly int insertion;
	private readonly int insertionLine;

	public MetadataDirectiveImport(string source, SongEditMetadata metadata)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(metadata);
		if (OpenSongParser.LooksLikeOpenSong(source) || source.StartsWith("%PDF-", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Metadata directives can only be imported into editable song text.");
		}

		this.source = source;
		List<(int Start, string Text, string Ending)> lines = ReadLines(source);
		this.newline = lines.Where(line => line.Ending.Length > 0).GroupBy(line => line.Ending)
			.OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault() ?? Environment.NewLine;
		Dictionary<string, List<MetadataDirectiveOccurrence>> occurrences = new(StringComparer.Ordinal);
		bool leading = true;
		int insertionOffset = 0, insertionNumber = 1;
		Entry? Parse(LineContext context)
		{
			ChordProDirectiveLine? directive = ChordProDirectiveLine.TryParse(context);
			var line = lines[context.LineNumber - 1];

			// Blank lines are skipped by DocumentParser; checking offsets also ends the leading block at a blank.
			leading &= line.Start == insertionOffset && directive is not null
				&& !directive.LongName.StartsWith("start_of_", StringComparison.OrdinalIgnoreCase);
			if (leading)
			{
				insertionOffset = line.Start + line.Text.Length + line.Ending.Length;
				insertionNumber = context.LineNumber + 1;
			}

			if (directive is not null && MetadataEntry.TryParse(directive) is MetadataEntry entry)
			{
				string name = NormalizeName(entry.Name);
				if (SupportedNames.Contains(name))
				{
					string value = entry.Argument;
					int close = line.Text.LastIndexOf('}');
					int valueStart = line.Text.LastIndexOf(value, close, StringComparison.Ordinal);
					bool safe = valueStart >= 0 && directive.QualifiedName.Selector is null && directive.Args.Attributes.Count == 0;
					if (!occurrences.TryGetValue(name, out List<MetadataDirectiveOccurrence>? list))
					{
						list = [];
						occurrences.Add(name, list);
					}

					list.Add(new(context.LineNumber, line.Start + Math.Max(0, valueStart), value.Length, value, line.Text, safe));
				}
			}

			return (Entry?)directive ?? BlankLine.Instance;
		}

		DocumentParser parser = new([Parse], [], tabWidth: null, structuredParsers: []);
		if (!string.IsNullOrWhiteSpace(source))
		{
			_ = Document.Parse(source, parser);
		}

		this.insertion = insertionOffset;
		this.insertionLine = insertionNumber;
		Dictionary<string, IReadOnlyList<string>> values = new(metadata.Scalars, StringComparer.Ordinal)
		{
			["title"] = [metadata.Title], ["artist"] = metadata.Artists, ["tag"] = metadata.Tags,
		};
		this.Proposals = [.. values.Where(pair => SupportedNames.Contains(pair.Key) && pair.Value.Any(value => !string.IsNullOrWhiteSpace(value)))
			.Select(pair => new MetadataDirectiveProposal(
				pair.Key,
				[.. pair.Value.Where(value => !string.IsNullOrWhiteSpace(value))],
				occurrences.TryGetValue(pair.Key, out List<MetadataDirectiveOccurrence>? existing) ? existing : []))];
	}

	public IReadOnlyList<MetadataDirectiveProposal> Proposals { get; }

	/// <summary>Builds exact edits. Ambiguous or conditional directives cannot be replaced implicitly.</summary>
	public IReadOnlyList<EditorTextChange> Preview(IReadOnlyList<MetadataDirectiveSelection> selections)
	{
		ArgumentNullException.ThrowIfNull(selections);
		List<EditorTextChange> changes = [];
		List<string> additions = [];
		HashSet<string> selected = new(StringComparer.Ordinal);
		foreach (MetadataDirectiveSelection selection in selections)
		{
			if (!selected.Add(selection.Name))
			{
				throw new ArgumentException("Select each metadata field only once.", nameof(selections));
			}

			MetadataDirectiveProposal proposal = this.Proposals.Single(item => item.Name == selection.Name);
			foreach (string value in proposal.Values)
			{
				if (value.IndexOfAny(['\r', '\n', '{', '}']) >= 0)
				{
					throw new InvalidOperationException($"{proposal.Name}: remove braces and line breaks before importing a scalar directive.");
				}
			}

			int firstAddition = 0;
			if (proposal.Occurrences.Count > 0)
			{
				int index = selection.OccurrenceIndex ?? throw new InvalidOperationException($"Choose the {proposal.Name} occurrence to update.");
				MetadataDirectiveOccurrence occurrence = proposal.Occurrences[index];
				if (!occurrence.CanReplace)
				{
					throw new InvalidOperationException($"Edit the conditional or structured {proposal.Name} directive directly in the source editor.");
				}

				if (occurrence.Value != proposal.Values[0])
				{
					changes.Add(new(occurrence.Start, occurrence.Length, occurrence.Value, proposal.Values[0], occurrence.LineNumber));
				}

				firstAddition = 1;
			}
			else if (selection.OccurrenceIndex is not null)
			{
				throw new ArgumentException("A missing directive has no occurrence to replace.", nameof(selections));
			}

			foreach (string value in proposal.Values.Skip(firstAddition))
			{
				additions.Add(proposal.Name is "tag" or "genre" ? $"{{meta: {proposal.Name} {value}}}" : $"{{{proposal.Name}: {value}}}");
			}
		}

		if (additions.Count > 0)
		{
			bool needsSeparator = this.insertion > 0 && this.source[this.insertion - 1] is not ('\r' or '\n');
			bool trailingNewline = this.insertion < this.source.Length || this.source.EndsWith('\r') || this.source.EndsWith('\n');
			string added = (needsSeparator ? this.newline : string.Empty) + string.Join(this.newline, additions)
				+ (trailingNewline ? this.newline : string.Empty);
			changes.Add(new(this.insertion, 0, string.Empty, added, this.insertionLine));
		}

		return [.. changes.OrderBy(change => change.Start)];
	}

	/// <summary>Applies only the chosen spans to a new buffer, leaving every unrelated character unchanged.</summary>
	public string Apply(IReadOnlyList<MetadataDirectiveSelection> selections)
	{
		StringBuilder result = new(this.source);
		foreach (EditorTextChange change in this.Preview(selections).Reverse())
		{
			result.Remove(change.Start, change.Length).Insert(change.Start, change.After);
		}

		return result.ToString();
	}

	private static string NormalizeName(string name) => name.ToLowerInvariant() switch { "t" => "title", "st" => "subtitle", _ => name.ToLowerInvariant() };

	private static List<(int Start, string Text, string Ending)> ReadLines(string source)
	{
		List<(int, string, string)> result = [];
		int start = 0;
		for (int index = 0; index < source.Length; index++)
		{
			if (source[index] is '\r' or '\n')
			{
				int end = index;
				if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n')
				{
					index++;
				}

				result.Add((start, source[start..end], source[end..(index + 1)]));
				start = index + 1;
			}
		}

		if (start < source.Length)
		{
			result.Add((start, source[start..], string.Empty));
		}

		return result;
	}
	#endregion
}
