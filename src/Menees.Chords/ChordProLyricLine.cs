﻿namespace Menees.Chords;

#region Using Directives

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
		Conditions.RequireReference(chords);

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
		Conditions.RequireReference(pair);

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

		// Remove trailing whitespace segment(s).
		while (segments.Count > 0 && segments[^1] is WhiteSpaceSegment)
		{
			segments.RemoveAt(segments.Count - 1);
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
		Conditions.RequireReference(context);

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

	#endregion

	#region Private Methods

	private static bool IsBracketed(string text) => text.StartsWith('[') && text.EndsWith(']');

	#endregion
}
