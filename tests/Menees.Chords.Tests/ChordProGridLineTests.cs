namespace Menees.Chords;

using Menees.Chords.Parsers;
using Shouldly;

[TestClass]
public class ChordProGridLineTests
{
	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("| A . . . |"); // Too short; not enough separators or chords
		Test("|--1-2-3---|--1-2-3---|"); // Tab line
		Test("| 1 . . . | 4 . . . | 5 . . . | 1 . . . |"); // Nashville numbered-chords are too much like tab lines.

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProGridLine? line = ChordProGridLine.TryParse(context);
			line.ShouldBeNull();
		}
	}

	[TestMethod]
	public void TryParseValidTest()
	{
		Test("| A . | D . | E . |", "A", "D", "E");
		Test("|| Am . . . | C . . . | D  . . . | F  . . . |", "Am", "C", "D", "F");
		Test("|  Am . . . | E . . . | Am . . . | Am . . . ||", "Am", "E", "Am", "Am");
		Test("|: C7 . | %  . :|: G7 . | % . :|   4x", "C7", "G7");

		static void Test(string text, params string[] chords)
		{
			Document doc = Document.Parse("{sog}\n" + text + "\n{eog}\n" + text, DocumentParserTests.Ungrouped());
			doc.Entries.Count.ShouldBe(4);
			doc.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().LongName.ShouldBe("start_of_grid");
			doc.Entries[1].ShouldBeOfType<ChordProGridLine>().ToString().ShouldBe(text);
			doc.Entries[2].ShouldBeOfType<ChordProDirectiveLine>().LongName.ShouldBe("end_of_grid");
			ChordProGridLine line = doc.Entries[3].ShouldBeOfType<ChordProGridLine>();
			line.ToString().ShouldBe(text);
			line.Segments.OfType<ChordSegment>().Select(cs => cs.Chord.Name).ShouldBe(chords);
		}
	}
}