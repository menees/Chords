namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class ChordProDirectiveLineTests
{
	[TestMethod]
	public void TryParseTest()
	{
		Test("{name}", "name");
		Test("{name: argument}", "name", "argument");
		Test("{name:argument}", "name", "argument");
		Test(" { name : argument } ", "name", "argument");

		Test("{start_of_tab: Solo}", "start_of_tab", "Solo", "sot");
		Test("{eot}", "eot", null, "end_of_tab");

		static void Test(string text, string? expectedName = null, string? expectedArgument = null, string? expectedAlternateName = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine? line = ChordProDirectiveLine.TryParse(context);

			if (expectedName is null)
			{
				line.ShouldBeNull();
			}
			else
			{
				line.ShouldNotBeNull();
				line.Name.ShouldBe(expectedName);
				line.Argument.ShouldBe(expectedArgument);

				if (expectedAlternateName is not null)
				{
					if (line.Name == line.LongName)
					{
						line.ShortName.ShouldBe(expectedAlternateName);
					}
					else
					{
						line.LongName.ShouldBe(expectedAlternateName);
					}
				}
			}
		}
	}

	[TestMethod]
	public void ToStringTest()
	{
		Test("{name}");
		Test("{name: argument}");
		Test("{name:argument}", "{name: argument}");
		Test(" { name : argument } ", "{name: argument}");

		static void Test(string text, string? expectedText = null)
		{
			LineContext context = LineContextTests.Create(text);
			ChordProDirectiveLine line = ChordProDirectiveLine.TryParse(context).ShouldNotBeNull();
			line.ToString().ShouldBe(expectedText ?? text);
		}
	}
}