namespace Menees.Chords;

[TestClass]
public class SectionTests
{
	[TestMethod]
	public void Section()
	{
		LyricLine line1 = new("Line 1");
		LyricLine line2 = new("Line 2");
		Section section = new(new[] { line1, line2 });
		section.Entries.ShouldBe(new[] { line1, line2 });
	}

	[TestMethod]
	public void ToStringTest()
	{
		LyricLine line1 = new("Line 1");
		LyricLine line2 = new("Line 2");
		Section section = new(new[] { line1, line2 });
		section.ToString().ShouldBe($"Line 1{Environment.NewLine}Line 2");
	}
}