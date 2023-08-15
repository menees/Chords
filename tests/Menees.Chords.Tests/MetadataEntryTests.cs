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
}