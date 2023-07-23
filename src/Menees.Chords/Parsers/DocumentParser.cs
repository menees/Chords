namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

#endregion

/// <summary>
/// Parses text to create <see cref="Section"/>s for a new <see cref="Document"/>.
/// </summary>
public sealed class DocumentParser
{
	#region Public Constants

	/// <summary>
	/// Gets the default number of spaces to use when expanding a tab character.
	/// </summary>
	public const int DefaultTabWidth = 4;

	#endregion

	#region Private Data Members

	private static readonly Func<LineContext, Entry?>[] DefaultLineParsers = new Func<LineContext, Entry?>[]
	{
		// TODO: Add other default line parsers in order. [Bill, 7/21/2023]
		HeaderLine.TryParse,
		Comment.TryParse,
		ChordProRemark.TryParse,
		ChordProDirective.TryParse,
		ChordProGridLine.TryParse,
		ChordProContent.TryParse,
		TablatureLine.TryParse,
		TextLine.Parse,
	};

	private readonly Func<LineContext, Entry?>[] lineParsers;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance with the specified options.
	/// </summary>
	/// <param name="lineParsers">An ordered array of line parsers to use. This allows you to customize the parsing
	/// for known input scenarios. For example, you can use a subset of ChordPro-compatible parsers if you know you're
	/// only reading ChordPro input, or you can omit <see cref="HeaderLine.TryParse"/> if you're not using
	/// "Ultimate Guitar"-style headers lines.
	/// <para/>
	/// If this parameter is null, then a default set of line parsers is used that tries to handle everything in a reasonable
	/// precendence order.</param>
	/// <param name="tabWidth">An optional tab width to use if tabs need to be converted to spaces. Pass null to
	/// skip converting tabs to spaces.</param>
	public DocumentParser(
		IEnumerable<Func<LineContext, Entry?>>? lineParsers = null,
		int? tabWidth = DefaultTabWidth)
	{
		this.lineParsers = lineParsers != null ? lineParsers.ToArray() : DefaultLineParsers;
		this.TabWidth = tabWidth;
	}

	#endregion

	#region Internal Properties

	internal int? TabWidth { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Converts tabs to spaces in the specified <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text to expand tabs in.</param>
	/// <param name="tabWidth">The number of spaces for a single tab character. If width is 0, then tabs are removed.</param>
	/// <returns><paramref name="text"/> with tabs expanded as spaces.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Tab width is negative</exception>
	[return: NotNullIfNotNull(nameof(text))]
	public static string? ConvertTabsToSpaces(string? text, int tabWidth)
	{
		if (tabWidth < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tabWidth), "Tab width must be non-negative.");
		}

		string? result = text;

#pragma warning disable CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'. string.Contains(char) isn't in .NET Framework 4.8.
		if (text != null && text.IndexOf('\t') >= 0)
#pragma warning restore CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'
		{
			StringBuilder sb = new(text.Length);
			foreach (char ch in text)
			{
				if (ch != '\t')
				{
					sb.Append(ch);
				}
				else if (tabWidth > 0)
				{
					// A tab should expand to at least one character.
					sb.Append(' ');

					// And it may expand up to TabWidth characters.
					while (sb.Length % tabWidth != 0)
					{
						sb.Append(' ');
					}
				}
			}

			result = sb.ToString();
		}

		return result;
	}

	#endregion

	#region Internal Methods

	internal IReadOnlyList<Entry> Parse(TextReader reader)
	{
		IReadOnlyList<Entry> result = this.ParseLines(reader);

		// TODO: Support custom section groupers. [Bill, 7/21/2023]
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
			string convertedLineText = this.TabWidth == null ? rawLineText : ConvertTabsToSpaces(rawLineText, this.TabWidth.Value);
			context.SetLine(convertedLineText);

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
