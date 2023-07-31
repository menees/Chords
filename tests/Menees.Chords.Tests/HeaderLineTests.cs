namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class HeaderLineTests
{
	[TestMethod]
	public void TryParseValid()
	{
		// "Alone With You - Outfield.docx" has some inside the brackets. Also, "Jack and Diane - John Mellencamp.txt"
		// "Line A Stone - Original.docx" has some outside the brackets. Also, "Authority Song - John Mellencamp.docx"
		Test("[Intro] (+ Hook)", "Intro", "(+ Hook)");
		Test("[Intro (with open \"D D\" string)]", "Intro (with open \"D D\" string)");
		Test("[Verse 1]", "Verse 1");
		Test("[Interlude (hammering chords)]", "Interlude (hammering chords)");
		Test("[Bridge (N.C.)]", "Bridge (N.C.)");
		Test("[Outro (heavy chords with open \"D D\" string)]", "Outro (heavy chords with open \"D D\" string)");
		Test("[Verse] (+ Hook)", "Verse", "(+ Hook)");
		Test("[Solo Lead – Relative to capo]", "Solo Lead – Relative to capo");
		Test("[Chorus] (a cappella with hand claps)", "Chorus", "(a cappella with hand claps)");

		static void Test(string text, string header, string? comment = null)
		{
			LineContext context = LineContextTests.Create(text);
			HeaderLine headerLine = HeaderLine.TryParse(context).ShouldNotBeNull();
			headerLine.Text.ShouldBe(header);
			if (comment is not null)
			{
				headerLine.Annotations.Count.ShouldBe(1);
				headerLine.Annotations[0].ShouldBeOfType<Comment>().ToString().ShouldBe(comment);
			}
		}
	}

	[TestMethod]
	public void TryParseInvalid()
	{
		Test("Not a header");
		Test("[A#]");
		Test("[Test] Plus");
		Test("[Verse][Chorus]");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			HeaderLine? headerLine = HeaderLine.TryParse(context);
			headerLine.ShouldBeNull();
		}
	}
}