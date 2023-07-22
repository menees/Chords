namespace Menees.Chords.Parsers;

[TestClass]
public sealed class DocumentParserTest
{
	[TestMethod]
	public void ConvertTabsToSpaces()
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
}
