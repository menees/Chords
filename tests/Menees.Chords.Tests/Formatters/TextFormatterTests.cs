namespace Menees.Chords.Formatters;

#region Using Directives

using System.Diagnostics;

#endregion

[TestClass]
public class TextFormatterTests
{
	#region Public Methods

	[TestMethod]
	public void TabIndentedTest()
	{
		Test("\t").ShouldContain("\t");
	}

	[TestMethod]
	public void SpecialIndentedTest()
	{
		Test("...@").ShouldContain("...@");
	}

	[TestMethod]
	public void UnindentedTest()
	{
		Test(null).ShouldNotContain("\t");
	}

	#endregion

	#region Private Methods

	private static string Test(string? indent)
	{
		Document document = TestUtility.LoadSwingLowSweetChariot();
		TextFormatter formatter = new(document, indent);
		string text = formatter.ToString();
		Debug.WriteLine(text);
		text.ShouldContain("{title: Swing Low Sweet Chariot}");
		string[] lines = text.Split('\n');
		lines.Length.ShouldBe(19);
		return text;
	}

	#endregion
}