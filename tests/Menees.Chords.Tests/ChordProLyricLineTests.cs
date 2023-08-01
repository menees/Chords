namespace Menees.Chords;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

[TestClass]
public class ChordProLyricLineTests
{
	#region Public Methods

	[TestMethod]
	public void TryParseValid()
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
		Test("  [C]   [C*] [G*]", "C", "C", "G");

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
	public void TryParseInvalid()
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

	#endregion
}