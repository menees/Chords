namespace Menees.Chords;

[TestClass]
public class TextEntryTests
{
	[TestMethod]
	public void Text()
	{
		const string Expected = " Testing\t";
		LyricLine line = new(Expected);
		line.Text.ShouldBe(Expected);
		line.ToString().ShouldBe(Expected);
	}
}