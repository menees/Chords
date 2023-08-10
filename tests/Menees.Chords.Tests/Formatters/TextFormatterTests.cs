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
		Format("\t").ShouldContain("\t");
	}

	[TestMethod]
	public void SpecialIndentedTest()
	{
		Format("...@").ShouldContain("...@");
	}

	[TestMethod]
	public void UnindentedTest()
	{
		Format(null).ShouldNotContain("\t");
	}

	#endregion

	#region Private Methods

	private static string Format(string? indent)
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