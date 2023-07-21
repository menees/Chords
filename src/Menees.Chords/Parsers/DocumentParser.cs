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

	private readonly Func<LineContext, Entry?>[] lineParsers;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	public DocumentParser(IEnumerable<Func<LineContext, Entry?>>? lineParsers = null)
	{
		// TODO: Use smart array of default line parsers [Bill, 7/21/2023]
		this.lineParsers = lineParsers != null ? lineParsers.ToArray() : Array.Empty<Func<LineContext, Entry?>>();
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

		int lineNumber = 0;
		string? lineText;
		while ((lineText = reader.ReadLine()) != null)
		{
			// Make line number 1-based.
			lineNumber++;
			context.SetLineInfo(lineText, lineNumber);

			if (string.IsNullOrWhiteSpace(lineText))
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
					throw new FormatException($"Line {lineNumber} could not be parsed: {lineText}");
				}
			}
		}

		return context.Entries;
	}

	#endregion
}
