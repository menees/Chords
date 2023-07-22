namespace Menees.Chords.Parsers;

[TestClass]
public sealed class LineContextTests
{
	[TestMethod]
	public void ConvertTabsToSpaces()
	{
		LineContext.ConvertTabsToSpaces(null, DocumentParser.DefaultTabWidth).ShouldBeNull();
		LineContext.ConvertTabsToSpaces(string.Empty, 3).ShouldBeEmpty();
		LineContext.ConvertTabsToSpaces("\t", DocumentParser.DefaultTabWidth).ShouldBe("    ");
		LineContext.ConvertTabsToSpaces("x\tx", DocumentParser.DefaultTabWidth).ShouldBe("x   x");
		LineContext.ConvertTabsToSpaces("xx\tx", DocumentParser.DefaultTabWidth).ShouldBe("xx  x");
		LineContext.ConvertTabsToSpaces("xxx\tx", DocumentParser.DefaultTabWidth).ShouldBe("xxx x");
		LineContext.ConvertTabsToSpaces("xxxx\tx", DocumentParser.DefaultTabWidth).ShouldBe("xxxx    x");
	}
}
