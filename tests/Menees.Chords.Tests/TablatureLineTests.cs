namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class TablatureLineTests
{
	[TestMethod]
	public void TryParseFormat()
	{
		Test("e|--1--", "e");
		Test("B|--1--|", "B");
		Test("F#:--1--:---2h3---:", "F#");

		Test("Note: This is a comment", null);
		Test("X|Y", null);

		static void Test(string text, string? expectedNote)
		{
			LineContext context = LineContextTests.Create(text);
			TablatureLine? line = TablatureLine.TryParse(context);
			if (expectedNote == null)
			{
				line.ShouldBeNull();
			}
			else
			{
				line.ShouldNotBeNull();
				line.NoteLength.ShouldBe(expectedNote.Length);
				line.Text[0..line.NoteLength].ShouldBe(expectedNote);
				line.Text.ShouldBe(text);
				line.ToString().ShouldBe(text);
			}
		}
	}

	[TestMethod]
	public void TryParseEnvironment()
	{
		Document doc = Document.Parse("{start_of_tab}\n|--1-2-3---|\n{end_of_tab}\nNot tab", DocumentParserTests.Ungrouped());
		doc.Entries.Count.ShouldBe(4);
		doc.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().Name.ShouldBe("start_of_tab");
		doc.Entries[1].ShouldBeOfType<TablatureLine>().Text.ShouldBe("|--1-2-3---|");
		doc.Entries[2].ShouldBeOfType<ChordProDirectiveLine>().Name.ShouldBe("end_of_tab");
		doc.Entries[3].ShouldBeOfType<LyricLine>().Text.ShouldBe("Not tab");
	}
}