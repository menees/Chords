namespace Menees.Chords;

using Menees.Chords.Parsers;
using Shouldly;

[TestClass]
public class DocumentTests
{
	[TestMethod]
	public void ChordProLineParsers()
	{
		DocumentParser parser = new DocumentParser(DocumentParser.ChordProLineParsers);
		Document document = Document.Parse(
			"""
			[Verse]
			# ChordPro remark
			[G]Well look what just walked do[C]wn the street to[G]day
			""",
			parser);
		document.Entries.Count.ShouldBe(3);

		// The ChordProLineParsers collection doesn't include HeaderLine, and "Verse" isn't a valid chord name.
		document.Entries[0].ShouldBeOfType<LyricLine>();
		document.Entries[1].ShouldBeOfType<ChordProRemarkLine>();
		document.Entries[2].ShouldBeOfType<ChordProLyricLine>();
	}

	[TestMethod]
	public void LoadFile()
	{
		Assert.Fail();
	}

	[TestMethod]
	public void LoadReader()
	{
		Assert.Fail();
	}

	[TestMethod]
	public void Parse()
	{
		Assert.Fail();
	}
}