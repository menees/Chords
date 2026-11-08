namespace Menees.Chords.Parsers;

[TestClass]
public sealed class DocumentParserTests
{
	#region Public Methods

	[TestMethod]
	[DataRow("{sortartist: Beatles, The}")]
	[DataRow("sortartist: Beatles, The")]
	[DataRow("{meta: sortartist Beatles, The}")]
	public void SortArtistIsRecognizedAsMetadata(string source)
	{
		Entry parsed = Document.Parse(source).Entries.Single();
		MetadataEntry? entry = parsed as MetadataEntry ?? MetadataEntry.TryParse((ChordProDirectiveLine)parsed);
		entry.ShouldNotBeNull();
		entry.Name.ShouldBe("sortartist");
		entry.Argument.ShouldBe("Beatles, The");
	}

	[TestMethod]
	public void ConvertTabsToSpacesTest()
	{
		DocumentParser.ConvertTabsToSpaces(null, DocumentParser.DefaultTabWidth).ShouldBeNull();
		DocumentParser.ConvertTabsToSpaces(string.Empty, 3).ShouldBeEmpty();
		DocumentParser.ConvertTabsToSpaces("x\tx", 0).ShouldBe("xx");

		DocumentParser.ConvertTabsToSpaces("\t", DocumentParser.DefaultTabWidth).ShouldBe("    ");
		DocumentParser.ConvertTabsToSpaces("x\tx", DocumentParser.DefaultTabWidth).ShouldBe("x   x");
		DocumentParser.ConvertTabsToSpaces("xx\tx", DocumentParser.DefaultTabWidth).ShouldBe("xx  x");
		DocumentParser.ConvertTabsToSpaces("xxx\tx", DocumentParser.DefaultTabWidth).ShouldBe("xxx x");
		DocumentParser.ConvertTabsToSpaces("xxxx\tx", DocumentParser.DefaultTabWidth).ShouldBe("xxxx    x");
	}

	#endregion

	#region Internal Methods

	internal static DocumentParser Ungrouped() => new(groupers: DocumentParser.Ungrouped);

	#endregion
}
