﻿namespace Menees.Chords.Parsers;

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

	private readonly Func<LineContext, Entry?>[] lineParsers;
	private readonly Func<GroupContext, IReadOnlyList<Entry>>[] groupers;

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
	/// <param name="groupers">An ordered array of grouping logic to use. This allows you to customize the way
	/// entries are grouped together (e.g., into <see cref="Section"/> and <see cref="ChordLyricPair"/>) entries.
	/// <para />
	/// If this is null, then a default set of groupers is used.</param>
	/// <param name="tabWidth">An optional tab width to use if tabs need to be converted to spaces. Pass null to
	/// skip converting tabs to spaces.</param>
	public DocumentParser(
		IEnumerable<Func<LineContext, Entry?>>? lineParsers = null,
		IEnumerable<Func<GroupContext, IReadOnlyList<Entry>>>? groupers = null,
		int? tabWidth = DefaultTabWidth)
	{
		this.lineParsers = lineParsers != null ? [.. lineParsers] : DefaultLineParsers;
		this.groupers = groupers != null ? [.. groupers] : DefaultGroupers;
		this.TabWidth = tabWidth;

		this.TryChordDefinitions = this.lineParsers.Contains(ChordDefinitions.TryParse);
		this.TryParseComment = this.lineParsers.Contains(Comment.TryParse);
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets a collection of line parsers to use when processing input that's known to be in ChordPro format.
	/// </summary>
	public static IEnumerable<Func<LineContext, Entry?>> ChordProLineParsers { get; }
		= [
			ChordProRemarkLine.TryParse,
			ChordProDirectiveLine.TryParse,
			ChordProGridLine.TryParse,
			ChordProLyricLine.TryParse,
			TablatureLine.TryParse,
			LyricLine.Parse,
		];

	/// <summary>
	/// Gets the default collection of line parsers.
	/// </summary>
	/// <remarks>
	/// These are designed to process input that's in human-friendly formats like
	/// "chords over text" or "Ultimate Guitar" format, but they can also process
	/// ChordPro format.
	/// </remarks>
	public static Func<LineContext, Entry?>[] DefaultLineParsers { get; } =
		[
			/* Add line parsers in order from most specific syntax to least specific syntax. */
			UriLine.TryParse,
			HeaderLine.TryParse,
			ChordProRemarkLine.TryParse, // This will parse #-prefixed lines before Comment.TryParse gets them.
			Comment.TryParse,
			ChordDefinitions.TryParse,
			ChordProDirectiveLine.TryParse,
			ChordProGridLine.TryParse,
			ChordProLyricLine.TryParse,
			TablatureLine.TryParse,
			ChordLine.TryParse,
			MetadataEntry.TryParse,
			TitleLine.TryParse,
			LyricLine.Parse,
		];

	/// <summary>
	/// Gets the default collection of <see cref="Entry"/> groupers.
	/// </summary>
	/// <remarks>
	/// These are used to group <see cref="Entry"/>s into <see cref="Section"/>s
	/// or other <see cref="IEntryContainer"/>s (e.g.., <see cref="ChordLyricPair"/>s)
	/// after the line parsers (e.g., <see cref="DefaultLineParsers"/> or <see cref="ChordProLineParsers"/>)
	/// convert the lines into <see cref="Entry"/>s.
	/// </remarks>
	public static Func<GroupContext, IReadOnlyList<Entry>>[] DefaultGroupers { get; }
		=
		[
			Parsers.GroupEntries.ByChordLinePair,
			Parsers.GroupEntries.ByChordProEnvironment,
			Parsers.GroupEntries.ByHeaderLine,
			Parsers.GroupEntries.ByBlankLine,
		];

	/// <summary>
	/// Gets an empty collection of groupers to use when processing input where grouping isn't desired.
	/// </summary>
	public static IEnumerable<Func<GroupContext, IReadOnlyList<Entry>>> Ungrouped { get; }
		= [];

	#endregion

	#region Internal Properties

	internal int? TabWidth { get; }

	internal bool TryChordDefinitions { get; }

	internal bool TryParseComment { get; }

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

		if (text != null && text.Contains('\t'))
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
		IReadOnlyList<Entry> lineEntries = this.ParseLines(reader);
		IReadOnlyList<Entry> result = this.GroupEntries(lineEntries);
		return result;
	}

	internal IReadOnlyList<Entry> GroupEntries(IReadOnlyList<Entry> entries)
	{
		IReadOnlyList<Entry> result = entries;

		GroupContext groupContext = new(this);
		foreach (Func<GroupContext, IReadOnlyList<Entry>> grouper in this.groupers)
		{
			groupContext.Entries = result;
			result = grouper(groupContext);
		}

		return result;
	}

	#endregion

	#region Private Methods

	private List<Entry> ParseLines(TextReader reader)
	{
		List<Entry> result = [];
		LineContext context = new(this);

		string? rawLineText;
		while ((rawLineText = reader.ReadLine()) != null)
		{
			string convertedLineText = this.TabWidth == null ? rawLineText : ConvertTabsToSpaces(rawLineText, this.TabWidth.Value);
			context.SetLine(convertedLineText);

			if (string.IsNullOrWhiteSpace(context.LineText))
			{
				result.Add(BlankLine.Instance);
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
						result.Add(entry);
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

		return result;
	}

	#endregion
}
