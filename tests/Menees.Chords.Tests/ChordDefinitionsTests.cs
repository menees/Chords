namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class ChordDefinitionsTests
{
	[TestMethod]
	public void TryParseValidTest()
	{
		Test1("A/C# _4222_", "A/C# x4222x");
		Test1("C# = x46664", "C# x46664");
		Test1("Em 022000 (Haunting)", "Em 022000", "(Haunting)");

		TestN(" C x32010, D/F# = 200232    ** Soft **", new[] { "C x32010", "D/F# 200232" }, new[] { "** Soft **" });
		TestN(" C x32010; D/F# = 200232;    ** Soft **", new[] { "C x32010", "D/F# 200232" }, new[] { "** Soft **" });
		TestN(" A/C#:  x4222x; D/F#:  2x0232 (use thumb for bass note)", new[] { "A/C# x4222x", "D/F# 2x0232" }, new[] { "(use thumb for bass note)" });

		static void Test1(string text, string? expectedDefinition, string? expectedComment = null)
			=> TestN(text, new[] { expectedDefinition ?? text }, expectedComment is null ? null : new[] { expectedComment });

		static void TestN(string text, string[] expectedDefinitions, string[]? expectedComments = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordDefinitions definitions = ChordDefinitions.TryParse(context).ShouldNotBeNull();
			definitions.Definitions.Select(def => def.ToString()).ShouldBe(expectedDefinitions);
			definitions.Annotations.Select(anno => anno.ToString()).ShouldBe(expectedComments ?? Array.Empty<string>());
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("C#");
		Test("This has A = x02220 in it.");
		Test("A, B, C");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			ChordDefinitions.TryParse(context).ShouldBeNull();
		}
	}
}