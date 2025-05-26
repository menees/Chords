namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class MetadataEntryTests
{
	[TestMethod]
	public void TryParseValidTest()
	{
		Test("title: This is a test");
		Test("artist: Big Mike");
		Test(" KEY : C# ", "key: C#");

		static void Test(string text, string? expected = null)
		{
			LineContext context = LineContextTests.Create(text);
			MetadataEntry? metadata = MetadataEntry.TryParse(context);
			metadata.ShouldNotBeNull();
			metadata.ToString().ShouldBe(expected ?? text);
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("title");
		Test("title: ");
		Test("name: argument");
		Test("Capo @ 4");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			MetadataEntry? metadata = MetadataEntry.TryParse(context);
			metadata.ShouldBeNull();
		}
	}

	[TestMethod]
	public void TryParseDirectiveValidTest()
	{
		Test("{title: This is a test}", "title", "This is a test");
		Test("{artist: Big Mike}", "artist", "Big Mike");

		Test("{meta: name value}", "name", "value");
		Test("{meta: artist The Beatles}", "artist", "The Beatles");
		Test("{ meta  composer  Andrew Lloyd Webber }", "composer", "Andrew Lloyd Webber");
		Test("{ meta  name='composer'  value='Andrew Lloyd Webber' }", "composer", "Andrew Lloyd Webber", 2);

		Test("{meta-data: name value}", "name", "value"); // "-data" is a conditional directive selector here.

		static void Test(string text, string expectedName, string expectedValue, int expectedAttributeCount = 0)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine directive = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
			MetadataEntry metadataEntry = MetadataEntry.TryParse(directive).ShouldNotBeNull();
			metadataEntry.Name.ShouldBe(expectedName);
			metadataEntry.Argument.ShouldBe(expectedValue);
			directive.Arguments.Attributes.Count.ShouldBe(expectedAttributeCount);
		}
	}

	[TestMethod]
	public void TryParseDirectiveInvalidTest()
	{
		Test("{meta}");
		Test("{meta: name}");
		Test("{metadata: name value}");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine directive = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
			MetadataEntry.TryParse(directive).ShouldBeNull();
		}
	}
}