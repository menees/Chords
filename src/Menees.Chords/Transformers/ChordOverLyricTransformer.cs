namespace Menees.Chords.Transformers;

#region Using Directives

using System;
using System.Collections.Generic;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Transforms a <see cref="Document"/> into "chords over text" format (e.g., like Ultimate Guitar format).
/// </summary>
public sealed class ChordOverLyricTransformer : DocumentTransformer
{
	#region Private Data Members

	private const StringComparison Comparison = ChordParser.Comparison;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified document.
	/// </summary>
	/// <param name="document">The document to transform.</param>
	public ChordOverLyricTransformer(Document document)
		: base(document)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Converts <see cref="DocumentTransformer.Document"/> into "chords over text" format.
	/// </summary>
	/// <returns>The current transformer.</returns>
	public override ChordOverLyricTransformer Transform()
	{
		IReadOnlyList<Entry> result = TransformEntries(this.Document.Entries);
		this.SetEntries(result);
		return this;
	}

#endregion

	#region Private Methods

	private static List<Entry> TransformEntries(IReadOnlyList<Entry> input)
	{
		List<Entry> output = new(input.Count);

		foreach (Entry entry in input)
		{
			switch (entry)
			{
				case ChordLyricPair pair:
					output.Add(pair);
					break;

				case IEntryContainer container:
					output.Add(new Section(TransformEntries(container.Entries), entry.Annotations));
					break;

				case ChordProDirectiveLine directive:
					TryConvertDirective(output, directive);
					break;

				case ChordProGridLine grid:
					// A ChordPro grid line isn't the same as a TablatureLine, so LyricLine is the best fit.
					output.Add(new LyricLine(grid.ToString(false).TrimEnd(), grid.Annotations));
					break;

				case ChordProLyricLine lyric:
					(ChordLine? chords, LyricLine? lyrics) = lyric.Split();
					if (chords != null)
					{
						output.Add(chords);
					}

					if (lyrics != null)
					{
						output.Add(lyrics);
					}

					break;

				case ChordProRemarkLine remark:
					output.Add(new Comment(remark.Text, annotations: remark.Annotations));
					break;

				default:
					output.Add(entry);
					break;
			}
		}

		return output;
	}

	private static void TryConvertDirective(List<Entry> output, ChordProDirectiveLine directive)
	{
		const string CommentPrefix = "** ";
		const string CommentSuffix = " **";
		string longName = directive.LongName;
		if (longName.StartsWith(ChordProEnvironment.EndPrefix, Comparison)
			|| longName.Equals(ChordProEnvironment.StartPrefix + ChordProEnvironment.GridName, Comparison)
			|| longName.Equals(ChordProEnvironment.StartPrefix + ChordProEnvironment.TabName, Comparison))
		{
			// We don't generate any output for these directives.
		}
		else if (longName == "comment")
		{
			output.Add(new Comment(directive.Argument ?? string.Empty, CommentPrefix, CommentSuffix, directive.Annotations));
		}
		else if (longName.StartsWith(ChordProEnvironment.StartPrefix, Comparison)
			&& longName.Length > ChordProEnvironment.StartPrefix.Length)
		{
			string header = directive.Argument ?? TitleLine.ToTitleCase(longName[ChordProEnvironment.StartPrefix.Length..]);
			output.Add(new HeaderLine(header, directive.Annotations));
		}
		else if (string.IsNullOrWhiteSpace(directive.Argument))
		{
			// Other argument-less directives are formatting related, so we'll just pass them through.
			output.Add(directive);
		}
		else if (longName.Equals("title", Comparison) || longName.Equals("artist", Comparison))
		{
			output.Add(new LyricLine(directive.Argument!, directive.Annotations));
		}
		else if (longName.Equals("tempo", Comparison))
		{
			output.Add(new LyricLine($"{directive.Argument} bpm"));
		}
		else if (longName.Equals("key", Comparison))
		{
			output.Add(new LyricLine($"Key: {directive.Argument}"));
		}
		else if (longName.Equals("capo", Comparison))
		{
			output.Add(new Comment($"Capo @ {directive.Argument}", CommentPrefix, CommentSuffix, directive.Annotations));
		}
		else if (longName.Equals("chord", Comparison) || longName.Equals("define", Comparison))
		{
			output.Add(ParseChordDirective(directive.Argument!, directive.Annotations));
		}
		else
		{
			// Other argument-ed directives are formatting related, so we'll just pass them through.
			output.Add(directive);
		}
	}

	private static Entry ParseChordDirective(string text, IReadOnlyList<Entry> annotations)
	{
		Entry result;

		// https://www.chordpro.org/chordpro/directives-chord/
		string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			result = BlankLine.Instance;
		}
		else
		{
			string initialSectionName = string.Empty;
			string name = parts[0];
			List<byte?> current = [];
			Dictionary<string, List<byte?>> sections = new(ChordParser.Comparer) { { initialSectionName, current } };
			foreach (string part in parts.Skip(1))
			{
				// If it's a fret or finger position, then add it to the current list.
				// Otherwise, make a new section with a new list.
				if (byte.TryParse(part, out byte value))
				{
					current.Add(value);
				}
				else if (ChordDefinition.IsUnplayedString(part))
				{
					current.Add(null);
				}
				else if (sections.TryGetValue(part, out List<byte?>? list))
				{
					current = list;
				}
				else
				{
					current = [];
					sections.Add(part, current);
				}
			}

			static Comment CreateComment(string comment, IReadOnlyList<Entry>? annotations = null)
				=> new(comment, "(", ")", annotations);

			if (!sections.TryGetValue("frets", out List<byte?>? frets))
			{
				// A chord directive can have just a name (e.g., {chord: Am}).
				result = CreateComment(text, annotations);
			}
			else
			{
				sections.Remove(nameof(frets));

				byte offset = 0;
				if (sections.TryGetValue("base-fret", out List<byte?>? baseFrets)
					&& baseFrets is [byte baseFret and >= 1])
				{
					sections.Remove("base-fret");
					offset = (byte)(baseFret - 1);
				}

				if (sections.TryGetValue("fingers", out List<byte?>? fingers))
				{
					sections.Remove("fingers");
				}

				List<byte?> absoluteFrets = [.. frets.Select(f => f is null ? null : (byte?)checked(f + offset))];
				ChordDefinition? definition = ChordDefinition.TryCreate(name, absoluteFrets, fingers);
				if (definition is null)
				{
					// The directive may have only unplayed strings, too few strings, or an unparsable chord name.
					result = CreateComment(text, annotations);
				}
				else
				{
					if (sections.TryGetValue(initialSectionName, out List<byte?>? initialSection)
						&& initialSection.Count == 0)
					{
						sections.Remove(initialSectionName);
					}

					// If there are leftover sections, then add them as a comment annotation.
					string annotationText = string.Join(" ", sections.OrderBy(pair => pair.Key)
						.Select(pair => $"{pair.Key} {string.Join(" ", pair.Value)}"));
					if (!string.IsNullOrEmpty(annotationText))
					{
						annotations = [.. annotations, CreateComment(annotationText)];
					}

					result = new ChordDefinitions([definition], annotations);
				}
			}
		}

		return result;
	}

	#endregion
}
