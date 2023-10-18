namespace Menees.Chords;

#region Using Directives

using System;
using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of interlaced lyrics and chords in ChordPro format.
/// </summary>
public sealed class ChordProLyricLine : SegmentedEntry
{
	#region Constructors

	private ChordProLyricLine(IReadOnlyList<TextSegment> segments)
		: base(segments)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Converts a <see cref="ChordLine"/> into a <see cref="ChordProLyricLine"/>
	/// (e.g., by bracketing the chord names).
	/// </summary>
	/// <param name="chords">The chord line to convert.</param>
	/// <returns>A new ChordPro line with bracketed chords.</returns>
	public static ChordProLyricLine Convert(ChordLine chords)
	{
		Conditions.RequireNonNull(chords);

		List<TextSegment> segments = new(chords.Segments.Count);

		foreach (TextSegment segment in chords.Segments)
		{
			if (segment is ChordSegment chord && !IsBracketed(chord.Text))
			{
				// Use original chord.Text in case it ends with an asterisk. We need
				// all of the original text in brackets, not just the chord name.
				segments.Add(new ChordSegment(chord.Chord, $"[{chord.Text}]"));
			}
			else
			{
				segments.Add(segment);
			}
		}

		ChordProLyricLine result = new(segments);
		result.AddAnnotations(chords.Annotations);
		return result;
	}

	/// <summary>
	/// Converts a <see cref="ChordLyricPair"/> into a <see cref="ChordProLyricLine"/>.
	/// (e.g., by bracketing the chord names and placing them inline with the lyrics).
	/// </summary>
	/// <param name="pair">The chords/lyrics pair to convert.</param>
	/// <returns>A new ChordPro lyric line with bracketed chords inline with lyrics.</returns>
	public static ChordProLyricLine Convert(ChordLyricPair pair)
	{
		Conditions.RequireNonNull(pair);

		List<TextSegment> segments = new();
		string lyricText = pair.Lyrics.Text;
		int lyricIndex = 0;

		// Examples:
		// A        G
		// All right now => [A]All right[G] now
		//            D/F#      A
		// Baby, it's all right now => Baby, it's [D/F#]all right [A]now
		foreach (TextSegment inputSegment in pair.Chords.Segments)
		{
			if (inputSegment is WhiteSpaceSegment whiteSpace)
			{
				AddLyrics(whiteSpace.Text.Length);
			}
			else
			{
				string originalText = inputSegment.Text;
				ChordSegment? chordSegment = inputSegment as ChordSegment;

				// Format non-chord text from the chord line as a ChordPro [*text] annotation.
				// https://www.chordpro.org/chordpro/chordpro-introduction/
				string marker = chordSegment is not null ? string.Empty : "*";
				string bracketed = IsBracketed(originalText) ? originalText : $"[{marker}{originalText}]";

				segments.Add(chordSegment is not null
					? new ChordSegment(chordSegment.Chord, bracketed)
					: new TextSegment(bracketed));
				AddLyrics(originalText.Length);
			}
		}

		if (lyricIndex < lyricText.Length)
		{
			AddLyrics(lyricText.Length - lyricIndex);
		}

		void AddLyrics(int length)
		{
			if (lyricIndex + length <= lyricText.Length)
			{
				segments.Add(TextSegment.Create(lyricText.Substring(lyricIndex, length)));
				lyricIndex += length;
			}
			else
			{
				// Whitespace is significant. See comments in ConvertChordLyricPair unit test.
				int remainingLyrics = Math.Max(0, lyricText.Length - lyricIndex);
				int padding = length - remainingLyrics;
				if (remainingLyrics > 0)
				{
					segments.Add(TextSegment.Create(lyricText.Substring(lyricIndex, remainingLyrics)));
				}

				segments.Add(new WhiteSpaceSegment(new string(' ', padding)));
				lyricIndex += remainingLyrics;
			}
		}

		// Remove trailing whitespace segment(s) if we don't have any annotations to append to formatted output.
		if (pair.Chords.Annotations.Count == 0 && pair.Lyrics.Annotations.Count == 0)
		{
			while (segments.Count > 0 && segments[^1] is WhiteSpaceSegment)
			{
				segments.RemoveAt(segments.Count - 1);
			}
		}

		ChordProLyricLine result = new(segments);
		result.AddAnnotations(pair.Chords.Annotations);
		result.AddAnnotations(pair.Lyrics.Annotations);
		return result;
	}

	/// <summary>
	/// Tries to parse the current line as a ChordPro content line (i.e., interlaced chords and lyrics).
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the line contains interlaced chords and lyrics.</returns>
	public static ChordProLyricLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		// If we see a bracketed token that's not a chord, then skip this line.
		// This doesn't try to handle ChordPro preprocessor conditional chords.
		// https://www.chordpro.org/chordpro/support-hints-and-tips/
		IReadOnlyList<TextSegment> segments = TryGetSegments(
			context,
			true,
			token => token.Type == TokenType.Text ? new TextSegment(token.Text) : null,
			out IReadOnlyList<Entry> annotations);

		// If there are no chords, then skip this line.
		ChordProLyricLine? result = null;
		if (segments.OfType<ChordSegment>().Any())
		{
			result = new(segments);
			result.AddAnnotations(annotations);
		}

		return result;
	}

	/// <summary>
	/// Separates the current ChordPro line into two lines in "chords over text" format.
	/// </summary>
	/// <returns>A tuple with separate chord and lyric lines. If there were no
	/// chords, the Chords result will be null. If there were no lyrics, the
	/// Lyrics result will be null.</returns>
	public (ChordLine? Chords, LyricLine? Lyrics) Split()
	{
		// Examples:
		//                        A        G
		// [A]All right[G] now => All right now
		//                                                 D/F#      A
		// Baby, it's [D/F#]all right [A]now => Baby, it's all right now
		int indentChord = 0;
		List<TextSegment> chordLineSegments = new();
		StringBuilder lyricLineText = new();
		foreach (TextSegment segment in this.Segments)
		{
			switch (segment)
			{
				case ChordSegment chord:
					AppendChord(chord.Text, unbracketed => new ChordSegment(chord.Chord, unbracketed));
					break;

				case WhiteSpaceSegment whitespace:
				default:
					if (IsBracketed(segment.Text))
					{
						// Put bracketed ChordPro annotations like [*↑] or [*D*] in the chord line.
						AppendChord(segment.Text, unbracketed => new TextSegment(unbracketed));
					}
					else
					{
						indentChord += segment.Text.Length;
						lyricLineText.Append(segment.Text);
					}

					break;
			}
		}

		void AppendChord(string chordText, Func<string, TextSegment> createChordSegment)
		{
			// Ensure there's at least some whitespace between chords.
			if (chordLineSegments.Count > 0 && chordLineSegments[^1] is not WhiteSpaceSegment)
			{
				indentChord = Math.Max(indentChord, 1);
			}

			if (indentChord > 0)
			{
				chordLineSegments.Add(new WhiteSpaceSegment(new string(' ', indentChord)));
				indentChord = 0;
			}

			int startIndex = chordText.StartsWith('[') ? (chordText.StartsWith("[*") ? 2 : 1) : 0;
			int endIndex = chordText.Length - (chordText.EndsWith(']') ? 1 : 0);
			chordText = chordText[startIndex..endIndex];
			chordLineSegments.Add(createChordSegment(chordText));
			indentChord = -chordText.Length;
		}

		// ChordProTransformer.AddAnnotations formats each annotation like [*Xxx]
		// for ChordProLyricLine, so we'll change their prefix and suffix.
		IEnumerable<Entry>? annotations = this.Annotations.Select(entry
			=> entry is Comment comment && comment.Prefix == "[*" && comment.Suffix == "]"
				? new Comment(comment.Text, "(", ")", comment.Annotations)
				: entry);
		ChordLine? chords = null;
		if (chordLineSegments.Count > 0)
		{
			chords = new(chordLineSegments, annotations);
			annotations = null;
		}

		LyricLine? lyrics = null;
		if (lyricLineText.Length > 0)
		{
			while (lyricLineText.Length > 0 && char.IsWhiteSpace(lyricLineText[^1]))
			{
				lyricLineText.Length--;
			}

			if (lyricLineText.Length > 0)
			{
				lyrics = new(lyricLineText.ToString(), annotations);
			}
		}

		return (chords, lyrics);
	}

	#endregion

	#region Private Methods

	private static bool IsBracketed(string text) => text.StartsWith('[') && text.EndsWith(']');

	#endregion
}
