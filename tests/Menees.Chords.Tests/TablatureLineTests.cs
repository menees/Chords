namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class TablatureLineTests
{
	[TestMethod]
	public void TryParseFormatTest()
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
	public void TryParseEnvironmentTest()
	{
		Document doc = Document.Parse("{start_of_tab}\n|--1-2-3---|\n{end_of_tab}\nNot tab");
		doc.Entries.Count.ShouldBe(2);

		Section tab = doc.Entries[0].ShouldBeOfType<Section>();
		tab.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().Name.ShouldBe("start_of_tab");
		tab.Entries[1].ShouldBeOfType<TablatureLine>().Text.ShouldBe("|--1-2-3---|");
		tab.Entries[2].ShouldBeOfType<ChordProDirectiveLine>().Name.ShouldBe("end_of_tab");

		doc.Entries[1].ShouldBeOfType<LyricLine>().Text.ShouldBe("Not tab");
	}
}