namespace Menees.Chords;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Menees.Chords.Parsers;

[TestClass]
public class ChordProMetaDirectiveLineTests
{
	[TestMethod]
	public void TryParseValidTest()
	{
		Test("{meta: name value}", "name", "value");
		Test("{meta: artist The Beatles}", "artist", "The Beatles");
		Test("{ meta  composer  Andrew Lloyd Webber }", "composer", "Andrew Lloyd Webber");
		Test("{ meta  name='composer'  value='Andrew Lloyd Webber' }", "composer", "Andrew Lloyd Webber", 2);

		static void Test(string text, string expectedName, string expectedValue, int expectedAttributeCount = 0)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProMetaDirectiveLine line = (ChordProDirectiveLine.TryParse(context) as ChordProMetaDirectiveLine).ShouldNotBeNull();
			line.MetadataName.ShouldBe(expectedName);
			line.MetadataValue.ShouldBe(expectedValue);
			line.Attributes.Count.ShouldBe(expectedAttributeCount);
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("{meta}");
		Test("{meta: name}");
		Test("{meta-data: name value}");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			(ChordProDirectiveLine.TryParse(context) as ChordProMetaDirectiveLine).ShouldBeNull();
		}
	}
}
