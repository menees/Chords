namespace Menees.Chords.Parsers;

#region Using Directives

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

#endregion

/// <summary>
/// Parses an OpenSong XML song document.
/// </summary>
public static class OpenSongParser
{
	#region Public Constants

	/// <summary>
	/// Gets the metadata name used for an OpenSong forced slide break.
	/// </summary>
	public const string SlideBreakMetadataName = "slide_break";

	#endregion

	#region Private Data Members

	private static readonly DocumentParser LyricsParser = new(
		[
			TryParseSectionHeader,
			Comment.TryParse,
			ChordDefinitions.TryParse,
			TablatureLine.TryParse,
			TryParseChordGridLine,
			ChordLine.TryParse,
			LyricLine.Parse,
		],
		structuredParsers: DocumentParser.Unstructured);

	#endregion

	#region Public Methods

	/// <summary>Checks whether text has the required OpenSong XML structure without parsing an XML document.</summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns>True if the text has an OpenSong song root with title and lyrics elements.</returns>
	public static bool LooksLikeOpenSong(ReadOnlySpan<char> text)
	{
		bool valid = true;
		text = TrimStart(text, includeBom: true);
		while (valid && (text.StartsWith("<?", StringComparison.Ordinal) || text.StartsWith("<!--", StringComparison.Ordinal)))
		{
			string terminator = text.StartsWith("<?", StringComparison.Ordinal) ? "?>" : "-->";
			int end = text.IndexOf(terminator, StringComparison.Ordinal);
			if (end < 0)
			{
				valid = false;
			}
			else
			{
				text = TrimStart(text[(end + terminator.Length)..], includeBom: false);
			}
		}

		bool result = valid
			&& HasStartTag(text, "song", requireAtStart: true)
			&& HasStartTag(text, "title")
			&& HasStartTag(text, "lyrics")
			&& text.Contains("</song>", StringComparison.Ordinal);
		return result;

		static bool HasStartTag(ReadOnlySpan<char> content, string name, bool requireAtStart = false)
		{
			ReadOnlySpan<char> marker = $"<{name}";
			int start = content.IndexOf(marker, StringComparison.Ordinal);
			bool found = start >= 0
				&& (!requireAtStart || start == 0)
				&& start + marker.Length < content.Length
				&& (char.IsWhiteSpace(content[start + marker.Length]) || content[start + marker.Length] is '>' or '/');
			return found;
		}

		static ReadOnlySpan<char> TrimStart(ReadOnlySpan<char> content, bool includeBom)
		{
			int start = 0;
			while (start < content.Length
				&& (char.IsWhiteSpace(content[start]) || (includeBom && content[start] == '\uFEFF')))
			{
				start++;
			}

			return content[start..];
		}
	}

	/// <summary>
	/// Tries to parse the structured context as an OpenSong XML song.
	/// </summary>
	/// <param name="context">The structured context to parse.</param>
	/// <returns>The parsed entries, or null if the context is not an OpenSong song.</returns>
	public static IReadOnlyList<Entry>? TryParse(StructuredContext context)
	{
		Conditions.RequireNonNull(context);

		IReadOnlyList<Entry>? result = null;
		if (context is StructuredContext<XDocument> { Data.Root: XElement root }
			&& root.Name.LocalName == "song")
		{
			IReadOnlyList<XElement> elements = [.. root.Elements()];
			bool hasTitle = elements.Any(element => element.Name.LocalName == "title");
			bool hasLyrics = elements.Any(element => element.Name.LocalName == "lyrics");
			if (hasTitle && hasLyrics)
			{
				List<Entry> entries = [];
				result = entries;
				foreach (XElement element in elements)
				{
					string name = element.Name.LocalName;
					if (name == "lyrics")
					{
						entries.AddRange(ParseLyrics(element.Value));
					}
					else
					{
						string value = element.Value.Trim();
						if (!string.IsNullOrEmpty(value))
						{
							entries.Add(new MetadataEntry(name, value));
						}
					}
				}
			}
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static List<Entry> ParseLyrics(string lyrics)
	{
		List<Entry> result = [];
		StringBuilder text = new(lyrics.Length);
		using StringReader reader = new(lyrics);
		string? line;
		while ((line = reader.ReadLine()) != null)
		{
			if (line.Length > 0 && (line[0] == '.' || line[0] == ' '))
			{
				line = line.Substring(1);
			}

			int slideBreakIndex;
			while ((slideBreakIndex = line.IndexOf("||", StringComparison.Ordinal)) >= 0)
			{
				text.AppendLine(line.Substring(0, slideBreakIndex));
				FlushText();
				result.Add(new MetadataEntry(SlideBreakMetadataName, bool.TrueString.ToLowerInvariant()));
				line = line.Substring(slideBreakIndex + 2);
			}

			text.AppendLine(line);
		}

		FlushText();
		return result;

		void FlushText()
		{
			if (text.Length > 0)
			{
				using StringReader textReader = new(text.ToString());
				result.AddRange(LyricsParser.Parse(textReader));
				text.Clear();
			}
		}
	}

	private static ChordProLyricLine? TryParseChordGridLine(LineContext context)
	{
		ChordProLyricLine? result = null;
		if (context.LineText.Contains('/'))
		{
			ChordLine? chordLine = ChordLine.TryParse(context);
			if (chordLine?.Segments.Any(IsRepeat) == true)
			{
				ChordProLyricLine converted = ChordProLyricLine.Convert(chordLine);
				List<TextSegment> segments = new(converted.Segments.Count);
				foreach (TextSegment segment in converted.Segments)
				{
					segments.Add(segment is ChordSegment or ChordAnnotationSegment or WhiteSpaceSegment
						? segment
						: new ChordAnnotationSegment($"[*{segment.Text}]"));
				}

				result = (ChordProLyricLine)new ChordProLyricLine(segments).Clone(converted.Annotations);
			}
		}

		return result;

		static bool IsRepeat(TextSegment segment)
			=> segment is not ChordSegment and not WhiteSpaceSegment
				&& segment.Text.All(character => character == '/');
	}

	private static HeaderLine? TryParseSectionHeader(LineContext context)
	{
		string text = context.LineText.Trim();
		HeaderLine? result = null;
		if (text.Length > 2 && text[0] == '[' && text[^1] == ']')
		{
			result = new HeaderLine(text.Substring(1, text.Length - 2));
		}

		return result;
	}

	#endregion
}
