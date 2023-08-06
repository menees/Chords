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
	#region Private Data Members

	private static readonly string SwingLogSweetChariotFileName = GetSampleFileName("Swing Low Sweet Chariot.cho");

	#endregion

	#region Public Methods

	[TestMethod]
	public void ChordProLineParsers()
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
	public void LoadFile()
	{
		Document document = Document.Load(SwingLogSweetChariotFileName);
		TestSwingLowSweetChariot(document);
	}

	[TestMethod]
	public void LoadReader()
	{
		using StreamReader reader = new(SwingLogSweetChariotFileName);
		Document document = Document.Load(reader);
		TestSwingLowSweetChariot(document);
	}

	[TestMethod]
	public void Parse()
	{
		string text = File.ReadAllText(SwingLogSweetChariotFileName);
		Document document = Document.Parse(text);
		TestSwingLowSweetChariot(document);
	}

	#endregion

	#region Private Methods

	private static string GetSampleFileName(string fileName)
		=> Path.Combine("Samples", fileName);

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