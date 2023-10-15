namespace Menees.Chords;

#region Using Directives

using System.Diagnostics;
using Menees.Chords.Parsers;

#endregion

[TestClass]
public class ChordProLyricLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseValidTest()
	{
		Test("Swing [D]low, sweet [G]chari[D]ot,", "D", "G", "D");
		Test("Comin’ for to carry me [A7]home.", "A7");
		Test("Swing [D7]low, sweet [G]chari[D]ot,", "D7", "G", "D");
		Test("Comin’ for to [A7]carry me [D]home.", "A7", "D");
		Test("I [D]looked over Jordan, and [G]what did I [D]see,", "D", "G", "D");
		Test("Comin’ for to carry me [A7]home.", "A7");
		Test("A [D]band of angels [G]comin’ after [D]me,", "D", "G", "D");
		Test("Comin’ for to [A7]carry me [D]home.", "A7", "D");
		Test("[E]Dreaming, [A/C#]Dreaming, [B]Just go on", "E", "A/C#", "B");
		Test("[Em]  [C]   [C/B]    [Am]", "Em", "C", "C/B", "Am");
		Test("  [C]   [C*] [G*]", "C", "C*", "G*");

		static ChordProLyricLine Test(string text, params string[] expectedChordNames)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProLyricLine? line = ChordProLyricLine.TryParse(context).ShouldNotBeNull(text);
			line.Segments.Count.ShouldBeGreaterThan(0);
			line.ToString().ShouldBe(text);
			line.Segments.OfType<ChordSegment>().Zip(expectedChordNames, (first, second) => (first, second))
				.All(pair => pair.first.Chord.Name == pair.second).ShouldBeTrue();
			return line;
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("A test line");
		Test("B# N.C.");
		Test("Am Q7");
		Test("  [A12]  ");

		// Ignore conditional chords that require preprocessor replacement.
		// https://www.chordpro.org/chordpro/support-hints-and-tips/
		Test("[A]He[A7-piano]llo, [Bm]World![C-keyboard]");
		Test("[A]Swe[A7-piano]et [Bm]Home![C-keyboard]");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProLyricLine.TryParse(context).ShouldBeNull();
		}
	}

	[TestMethod]
	public void ConvertChordLineTest()
	{
		Test(" D7        G  ", " [D7]        [G]  ");
		Test("C       C*   G*", "[C]       [C*]   [G*]");
		Test("A Bb B   (Half steps)", "[A] [Bb] [B]   (Half steps)");

		static void Test(string text, string expectedText)
		{
			LineContext context = LineContextTests.Create(text);
			ChordLine line = ChordLine.TryParse(context).ShouldNotBeNull(text);
			ChordProLyricLine actual = ChordProLyricLine.Convert(line);
			actual.ToString().ShouldBe(expectedText);
		}
	}

	[TestMethod]
	public void ConvertChordLyricPairTest()
	{
		// Chords and lyrics start together.
		Test(
			"""
			A        G
			All right now
			""",
			"[A]All right[G] now");

		// Lyrics start before chords. Lyrics end after chords.
		Test(
			"""
			           D/F#      A
			Baby, it's all right now
			""",
			"Baby, it's [D/F#]all right [A]now");

		// Chords start before lyrics. Chords end after lyrics.
		// The leading lyric whitespace is significant. We need to preserve WS gaps wide
		// enough for the chords to go over when the ChordProLyricLine is rendered as
		// chords-over-text. Technically, we could collapse the WS between two chords
		// (e.g., between [A5] and [Dadd11] in this example), but we can't collapse WS
		// between chords and text (e.g., [A5] and "There"). Most renderers would expand
		// the WS between two chords so they didn't overlap, but we must have two spaces
		// to ensure that A5 displays over the spaces and prior to the lyrics starting.
		Test(
			"""
			A5 Dadd11 D/A A5          A5       D/F#      A5
			                There she stood in the street
			""",
			"[A5]   [Dadd11]       [D/A]    [A5]  There she [A5]stood in [D/F#]the street[A5]");

		// Chords overlap lyric word boundaries.
		Test(
			"""
			Dadd11   Cmaj7   A#m7
			   Overlap is hard.
			""",
			"[Dadd11]   Overla[Cmaj7]p is har[A#m7]d.");

		// Lyrics start before chords. Lyrics end after chords. One chord is mid-word.
		Test(
			"""
			      D          G    D
			Swing low, sweet chariot,
			""",
			"Swing [D]low, sweet [G]chari[D]ot,");

		// Use markup and annotations on each line.
		Test(
			"""
			      D ↓        G↑   D*  (* Use higher D second time) D* = x57775
			Swing low, sweet chariot,  ** Sing "low" as bass **
			""",
			"Swing [D]lo[*↓]w, sweet [G↑]chari[D*]ot,  (* Use higher D second time) D* x57775 ** Sing \"low\" as bass **");

		static void Test(string chordLyricLines, string expectedText)
		{
			Document doc = Document.Parse(chordLyricLines);
			doc.Entries.Count.ShouldBe(1);
			ChordLyricPair pair = doc.Entries[0].ShouldBeOfType<ChordLyricPair>();
			ChordProLyricLine actual = ChordProLyricLine.Convert(pair);
			actual.ToString().ShouldBe(expectedText);
		}
	}

	[TestMethod]
	public void SplitTest()
	{
		Test(
			"[A]All right[G] now",
			"A        G",
			"All right now");

		Test(
			"Baby, it's [D/F#]all right [A]now",
			"           D/F#      A",
			"Baby, it's all right now");

		Test(
			"[A5]   [Dadd11]       [D/A]    [A5]  There she [A5]stood in [D/F#]the street[A5]",
			"A5 Dadd11 D/A A5          A5       D/F#      A5",
			"                There she stood in the street");

		Test(
			"[Dadd11]   Overla[Cmaj7]p is har[A#m7]d.",
			"Dadd11   Cmaj7   A#m7",
			"   Overlap is hard.");

		Test(
			"Swing [D]low, sweet [G]chari[D]ot,",
			"      D          G    D",
			"Swing low, sweet chariot,");

		Test(
			"Swing [D]lo[*↓]w, sweet [G↑]chari[D*]ot,  (* Use higher D second time) D* x57775 ** Sing \"low\" as bass **",
			"      D ↓        G↑   D* (* Use higher D second time) D* x57775 ** Sing \"low\" as bass **",
			"Swing low, sweet chariot,");

		static void Test(string text, string? expectedChords, string? expectedLyrics)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProLyricLine line = ChordProLyricLine.TryParse(context).ShouldNotBeNull();
			(ChordLine? chords, LyricLine? lyrics) = line.Split();
			chords?.ToString().ShouldBe(expectedChords);
			lyrics?.ToString().ShouldBe(expectedLyrics);
		}
	}

	[TestMethod]
	public void ConvertAndSplitTest()
	{
		int pairs = 0;
		int lines = 0;
		foreach (Document document in TestUtility.SampleDocuments)
		{
			TryConvertAndSplit(document.Entries);

			void TryConvertAndSplit(IEnumerable<Entry> entries)
			{
				foreach (Entry entry in entries)
				{
					switch (entry)
					{
						case ChordLyricPair pair:
							{
								ChordProLyricLine line = ChordProLyricLine.Convert(pair);
								(ChordLine? chords, LyricLine? lyrics) = line.Split();
								chords.ShouldNotBeNull(document.FileName);
								lyrics.ShouldNotBeNull(document.FileName);

								chords.ToString().ShouldBe(pair.Chords.ToString(), document.FileName);
								lyrics.ToString().ShouldBe(pair.Lyrics.ToString(), document.FileName);
								pairs++;
							}

							break;

						case ChordProLyricLine line:
							{
								(ChordLine? chords, LyricLine? lyrics) = line.Split();
								if (chords != null && lyrics != null)
								{
									ChordLyricPair newPair = new(chords, lyrics);
									ChordProLyricLine newLine = ChordProLyricLine.Convert(newPair);
									newLine.ToString().ShouldBe(line.ToString(), document.FileName);
									lines++;
								}
							}

							break;

						case IEntryContainer container:
							TryConvertAndSplit(container.Entries);
							break;
					}
				}
			}
		}

		Debug.WriteLine($"Round-trip tested {pairs} ChordLyricPairs and {lines} ChordProLyricLines.");
	}

	#endregion
}