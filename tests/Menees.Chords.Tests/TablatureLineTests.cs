namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class TablatureLineTests
{
	[TestMethod]
	public void TryParse()
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
}