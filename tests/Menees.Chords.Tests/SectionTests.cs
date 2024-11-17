namespace Menees.Chords;

[TestClass]
public class SectionTests
{
	[TestMethod]
	public void SectionTest()
	{
		LyricLine line1 = new("Line 1");
		LyricLine line2 = new("Line 2");
		Section section = new([line1, line2]);
		section.Entries.ShouldBe([line1, line2]);
	}

	[TestMethod]
	public void ToStringTest()
	{
		LyricLine line1 = new("Line 1");
		LyricLine line2 = new("Line 2");
		Section section = new([line1, line2]);
		section.ToString().ShouldBe($"Line 1{Environment.NewLine}Line 2");
	}
}