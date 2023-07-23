namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class LyricLineTests
{
	[TestMethod]
	public void LyricLineTest()
	{
		LyricLine line = new("Test");
		line.Text.ShouldBe("Test");
	}

	[TestMethod]
	public void Parse()
	{
		LineContext context = LineContextTests.Create("Testing");
		LyricLine.Parse(context).Text.ShouldBe("Testing");
	}
}