namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class ChordProRemarkLineTests
{
	[TestMethod]
	public void TryParseTest()
	{
		Test("Not a remark", false);
		Test("# Remark", true);
		Test("  # Remark  ", true);

		static void Test(string text, bool expectLine)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProRemarkLine? line = ChordProRemarkLine.TryParse(context);
			if (!expectLine)
			{
				line.ShouldBeNull();
			}
			else
			{
				line.ShouldNotBeNull();
				line.Text.ShouldBe(text);
			}
		}
	}
}