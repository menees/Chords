namespace Menees.Chords.Transformers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Transforms a <see cref="Document"/> into "chords over text" format (e.g., like Ultimate Guitar format).
/// </summary>
public sealed class ChordOverLyricTransformer : DocumentTransformer
{
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
	public override
#if NET // Modern .NET is required for C#9 covariant returns.
		ChordOverLyricTransformer
#else
		DocumentTransformer
#endif
		Transform()
	{
		IReadOnlyList<Entry> result = TransformEntries(this.Document.Entries);
		this.SetEntries(result);
		return this;
	}

#endregion

	#region Private Methods

	private static IReadOnlyList<Entry> TransformEntries(IReadOnlyList<Entry> input)
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
					output.Add(new LyricLine(grid.ToString(false), grid.Annotations));
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

	#endregion

	#region Private Methods

	private static void TryConvertDirective(List<Entry> output, ChordProDirectiveLine directive)
	{
		const StringComparison Comparison = ChordParser.Comparison;
		const string StartOf = "start_of_";

		string longName = directive.LongName;
		if (longName.StartsWith("end_of_", Comparison)
			|| longName.Equals("start_of_grid", Comparison)
			|| longName.Equals("start_of_tab", Comparison))
		{
			// We don't generate any output for these directives.
		}
		else if (longName == "comment")
		{
			output.Add(new Comment(directive.Argument ?? string.Empty, "** ", " **", directive.Annotations));
		}
		else if (longName.StartsWith(StartOf, Comparison) && longName.Length > StartOf.Length)
		{
			string header = directive.Argument ?? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(longName[StartOf.Length..]);
			output.Add(new HeaderLine($"[{header}]", directive.Annotations));
		}
		else if ((longName.Equals("title", Comparison) || longName.Equals("artist", Comparison))
			&& !string.IsNullOrEmpty(directive.Argument))
		{
			output.Add(new LyricLine(directive.Argument!, directive.Annotations));
		}
		else if (longName.Equals("tempo", Comparison) && !string.IsNullOrEmpty(directive.Argument))
		{
			output.Add(new LyricLine($"{directive.Argument} bpm"));
		}
		else if (longName.Equals("key", Comparison) && !string.IsNullOrEmpty(directive.Argument))
		{
			output.Add(new LyricLine($"Key: {directive.Argument}"));
		}
		else
		{
			// Other directives are formatting related, so we'll just pass them through.
			output.Add(directive);
		}
	}

	#endregion
}
