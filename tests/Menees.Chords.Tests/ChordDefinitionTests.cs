namespace Menees.Chords;

[TestClass]
public class ChordDefinitionTests
{
	[TestMethod]
	public void TryParseValid()
	{
		Test("Am", "x02210", null, 0, 2, 2, 1, 0);
		Test("A/C#", "x42220", null, 4, 2, 2, 2, 0);
		Test("G7", "320001", 3, 2, 0, 0, 0, 1);

		static void Test(string name, string defintion, params byte?[] expectedDefinition)
		{
			ChordDefinition chordDefinition = ChordDefinition.TryParse(name, defintion).ShouldNotBeNull();
			chordDefinition.Chord.Name.ShouldBe(name);
			chordDefinition.Definition.ShouldBe(expectedDefinition);
		}
	}

	[TestMethod]
	public void TryParseInvalid()
	{
		ChordDefinition.TryParse("?", "0000").ShouldBeNull();
		ChordDefinition.TryParse("Open", "0000").ShouldBeNull();
		ChordDefinition.TryParse("C#", "xyxy_x").ShouldBeNull();
	}

	[TestMethod]
	public void ToStringTest()
	{
		ChordDefinition.TryParse("C", "x-3-2-0-1-0").ShouldNotBeNull().ToString().ShouldBe("C x32010");
		ChordDefinition.TryParse("D/F#", "200121").ShouldNotBeNull().ToString().ShouldBe("D/F# 200121");
	}
}