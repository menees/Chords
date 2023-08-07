namespace Menees.Chords;

[TestClass]
public class ChordDefinitionTests
{
	[TestMethod]
	public void TryParseValidTest()
	{
		Test("Am", "x02210", null, 0, 2, 2, 1, 0);
		Test("A/C#", "x42220", null, 4, 2, 2, 2, 0);
		Test("A/C#", "_4222_", null, 4, 2, 2, 2, null);
		Test("G7", "320001", 3, 2, 0, 0, 0, 1);
		Test("C", "x-3-2-0-1-0", null, 3, 2, 0, 1, 0);
		Test("D/F#", "200232", 2, 0, 0, 2, 3, 2);
		Test("A/C#", "_4222_", null, 4, 2, 2, 2, null);
		Test("Em", "12-14-14-13-12-12", 12, 14, 14, 13, 12, 12);

		static void Test(string name, string defintion, params byte?[] expectedDefinition)
		{
			ChordDefinition chordDefinition = ChordDefinition.TryParse(name, defintion).ShouldNotBeNull();
			chordDefinition.Chord.Name.ShouldBe(name);
			chordDefinition.Definition.ShouldBe(expectedDefinition);
			string separator = expectedDefinition.Any(num => num >= 10) ? "-" : string.Empty;
			chordDefinition.ToString().ShouldBe(name + " " + string.Join(separator, expectedDefinition.Select(num => num?.ToString() ?? "x")));
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		ChordDefinition.TryParse("?", "0000").ShouldBeNull();
		ChordDefinition.TryParse("Open", "0000").ShouldBeNull();
		ChordDefinition.TryParse("C#", "xyxy_x").ShouldBeNull();
	}
}