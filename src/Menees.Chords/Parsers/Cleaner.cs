namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#endregion

/// <summary>
/// Used to clean up text before processing it as a <see cref="Document"/>.
/// </summary>
/// <remarks>
/// This class:
/// <list type="bullet">
/// <item>Trims trailing whitespace from each line</item>
/// <item>Removes alternating blank lines</item>
/// <item>Removes consecutive blank lines</item>
/// <item>Ensures blank line before [Section] header</item>
/// <item>Removes blank line after [Section] header</item>
/// <item>Removes leading and trailing blank lines</item>
/// <item>Removes known useless trailing lines (e.g., X, Set8)</item>
/// </list>
/// </remarks>
public sealed class Cleaner
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The text to clean.</param>
	public Cleaner(string text)
	{
		this.OriginalText = text;
		this.CleanText = this.Clean();
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the original text passed to the constructor.
	/// </summary>
	public string OriginalText { get; }

	/// <summary>
	/// Gets the cleaned text.
	/// </summary>
	public string CleanText { get; }

	#endregion

	#region Private Methods

	private static bool TryRemoveAlternativeBlankLines(List<string> lines, Predicate<int> checkLine)
	{
		bool result = true;

		int numLines = lines.Count;
		List<string> scrubbed = new((numLines / 2) + 1);
		for (int index = 0; index < numLines; index++)
		{
			string line = lines[index];
			if (!checkLine(index))
			{
				scrubbed.Add(line);
			}
			else if (!string.IsNullOrEmpty(line))
			{
				result = false;
				break;
			}
		}

		if (result)
		{
			lines.Clear();
			lines.AddRange(scrubbed);
		}

		return result;
	}

	private static void RemoveConsecutiveBlankLines(List<string> lines)
	{
		bool wasPreviousLineBlank = false;
		List<string> scrubbed = new(lines.Count);
		foreach (string line in lines)
		{
			if (!string.IsNullOrEmpty(line))
			{
				wasPreviousLineBlank = false;
				scrubbed.Add(line);
			}
			else if (!wasPreviousLineBlank)
			{
				wasPreviousLineBlank = true;
				scrubbed.Add(line);
			}
		}

		if (scrubbed.Count < lines.Count)
		{
			lines.Clear();
			lines.AddRange(scrubbed);
		}
	}

	private static void NormalizeSections(List<string> lines)
	{
		DocumentParser parser = new(
			[HeaderLine.TryParse, LyricLine.Parse],
			[GroupEntries.ByHeaderLine]);
		Document document = Document.Parse(string.Join(Environment.NewLine, lines), parser);
		lines.Clear();
		foreach (Entry entry in document.Entries)
		{
			AddEntryLines(entry);
		}

		void AddEntryLines(Entry entry)
		{
			if (entry is IEntryContainer container)
			{
				bool checkForPostHeaderBlankLine = false;
				foreach (Entry subentry in container.Entries)
				{
					if (!checkForPostHeaderBlankLine || subentry is not BlankLine)
					{
						AddEntryLines(subentry);
						checkForPostHeaderBlankLine = false;
					}

					if (subentry is HeaderLine)
					{
						checkForPostHeaderBlankLine = true;
					}
				}
			}
			else
			{
				if (entry is HeaderLine && lines.Count > 0 && !string.IsNullOrEmpty(lines[^1]))
				{
					lines.Add(string.Empty);
				}

				lines.Add(entry.ToString());
			}
		}
	}

	private static void RemoveLeadingAndTrailingBlankLines(List<string> lines)
	{
		// Trailing blank lines are more common than leading blank lines, so remove them first.
		while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
		{
			lines.RemoveAt(lines.Count - 1);
		}

		while (lines.Count > 0 && string.IsNullOrEmpty(lines[0]))
		{
			// This is an inefficient O(N) removal, but it's simple and should be rare.
			lines.RemoveAt(0);
		}
	}

	private static void CleanTitle(List<string> lines)
	{
		if (lines.Count > 0)
		{
			// Try URI parsing before Title parsing.
			DocumentParser parser = new([UriLine.TryParse, TitleLine.TryParse]);
			Document document = Document.Parse(lines[0], parser);
			if (document.Entries.Count > 0)
			{
				if (document.Entries[0] is TitleLine titleLine)
				{
					lines[0] = titleLine.ToMetadataString();
				}
				else if (document.Entries[0] is UriLine uriLine)
				{
					TitleLine? uriTitle = TitleLine.TryParse(uriLine.Uri);
					if (uriTitle != null)
					{
						lines.Insert(0, uriTitle.ToString());
					}
				}
			}
		}
	}

	private string Clean()
	{
		List<string> lines = [];
		using (StringReader reader = new(this.OriginalText ?? string.Empty))
		{
			string? line;
			while ((line = reader.ReadLine()) != null)
			{
				// Trim trailing whitespace.
				lines.Add(line.TrimEnd());
			}
		}

		// Remove alternating blank lines, e.g., when copying an Ultimate Guitar transcription
		// from Firefox. Try odd lines first. If that fails, then try even lines.
		if (!TryRemoveAlternativeBlankLines(lines, index => index % 2 == 1))
		{
			TryRemoveAlternativeBlankLines(lines, index => index % 2 == 0);
		}

		RemoveConsecutiveBlankLines(lines);
		NormalizeSections(lines);
		RemoveLeadingAndTrailingBlankLines(lines);

		HashSet<string> badTrailingLines = new(ChordParser.Comparer) { "X", "Set8" };
		while (lines.Count > 0 && badTrailingLines.Contains(lines[^1]))
		{
			lines.RemoveAt(lines.Count - 1);
		}

		CleanTitle(lines);

		string result = string.Join(Environment.NewLine, lines);
		return result;
	}

	#endregion
}
