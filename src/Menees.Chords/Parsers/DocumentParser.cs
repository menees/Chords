namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#endregion

/// <summary>
/// Parses text to create <see cref="Section"/>s for a new <see cref="Document"/>.
/// </summary>
public sealed class DocumentParser
{
	#region Private Data Members

	private static readonly Func<LineContext, Entry?>[] DefaultLineParsers = new Func<LineContext, Entry?>[]
	{
		// TODO: Add other default line parsers in order. [Bill, 7/21/2023]
		HeaderLine.TryParse,
		TextLine.Parse,
	};

	private readonly Func<LineContext, Entry?>[] lineParsers;
	private readonly int? tabWidth;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance with the specified options.
	/// </summary>
	/// <param name="lineParsers">An ordered array of line parsers to use.</param>
	/// <param name="tabWidth">An optional tab width to use if tabs need to be converted to spaces.</param>
	public DocumentParser(
		IEnumerable<Func<LineContext, Entry?>>? lineParsers = null,
		int? tabWidth = null)
	{
		this.lineParsers = lineParsers != null ? lineParsers.ToArray() : DefaultLineParsers;
		this.tabWidth = tabWidth;
	}

	#endregion

	#region Internal Methods

	internal List<Section> Parse(TextReader reader)
	{
		IReadOnlyList<Entry> entries = this.ParseLines(reader);

		// TODO: Finish Parse. [Bill, 7/21/2023]
		Section section = new(entries.ToList());

		List<Section> result = new() { section };
		return result;
	}

	#endregion

	#region Private Methods

	private IReadOnlyList<Entry> ParseLines(TextReader reader)
	{
		LineContext context = new(this);

		string? rawLineText;
		while ((rawLineText = reader.ReadLine()) != null)
		{
			context.SetLineInfo(rawLineText, this.tabWidth);

			if (string.IsNullOrWhiteSpace(context.LineText))
			{
				context.Add(new BlankLine());
			}
			else
			{
				bool parsed = false;
				foreach (Func<LineContext, Entry?> tryParse in this.lineParsers)
				{
					Entry? entry = tryParse(context);
					if (entry != null)
					{
						parsed = true;
						context.Add(entry);
						break;
					}
				}

				if (!parsed)
				{
					// We should only get here if TextLine.Parse wasn't used (since it accepts everything).
					throw new FormatException($"Line {context.LineNumber} could not be parsed: {context.LineText}");
				}
			}
		}

		return context.Entries;
	}

	#endregion
}
