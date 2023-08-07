namespace Menees.Chords;

#region Using Directives

using System;
using System.IO;
using Menees.Chords.Parsers;
using Shouldly;

#endregion

[TestClass]
public class DocumentTests
{
	#region Public Methods

	[TestMethod]
	public void ChordProLineParsersTest()
	{
		DocumentParser parser = new(DocumentParser.ChordProLineParsers);
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
	public void LoadFileTest()
	{
		Document document = Document.Load(TestUtility.SwingLowSweetChariotFileName);
		TestSwingLowSweetChariot(document);
	}

	[TestMethod]
	public void LoadReaderTest()
	{
		using StreamReader reader = new(TestUtility.SwingLowSweetChariotFileName);
		Document document = Document.Load(reader);
		TestSwingLowSweetChariot(document);
	}

	[TestMethod]
	public void ParseTest()
	{
		string text = File.ReadAllText(TestUtility.SwingLowSweetChariotFileName);
		Document document = Document.Parse(text);
		TestSwingLowSweetChariot(document);
	}

	#endregion

	#region Private Methods

	private static void TestSwingLowSweetChariot(Document document)
	{
		document.Entries.Count.ShouldBe(9);

		document.Entries[0].ShouldBeOfType<Section>()
			.Entries[0].ShouldBeOfType<Comment>()
			.Text.ShouldBe("A simple ChordPro song.");

		document.Entries[8].ShouldBeOfType<ChordProDirectiveLine>()
			.Name.ShouldBe("comment");
	}

	#endregion
}