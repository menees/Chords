namespace Menees.Chords;

using Menees.Chords.Parsers;
using Shouldly;

[TestClass]
public class ChordProGridLineTests
{
	[TestMethod]
	public void TryParseFail()
	{
		const string Text = "|| A . . . | E . . . |";
		LineContext context = LineContextTests.Create(Text);

		// ChordPro grid line syntax is ambiguous enough that we don't try to parse.
		// We just detect if we're inside a start_of_grid/end_of_grid environment.
		ChordProGridLine? line = ChordProGridLine.TryParse(context);
		line.ShouldBeNull();
	}

	[TestMethod]
	public void TryParseSuccess()
	{
		const string Text = "| A . | D . | E . |";
		Document doc = Document.Parse("{sog}\n" + Text + "\n{eog}\n" + Text);
		doc.Entries.Count.ShouldBe(4);
		doc.Entries[0].ShouldBeOfType<ChordProDirectiveLine>().LongName.ShouldBe("start_of_grid");
		doc.Entries[1].ShouldBeOfType<ChordProGridLine>().Text.ShouldBe(Text);
		doc.Entries[2].ShouldBeOfType<ChordProDirectiveLine>().LongName.ShouldBe("end_of_grid");
		doc.Entries[3].ShouldBeOfType<LyricLine>().Text.ShouldBe(Text);
	}
}