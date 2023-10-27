namespace Menees.Chords;

using Menees.Chords.Parsers;

[TestClass]
public class UriLineTests
{
	[TestMethod]
	public void TryParseValidTest()
	{
		Test("https://github.com/menees/Chords/commits/master");
		Test(" https://learn.microsoft.com/en-us/dotnet/core/install/windows?tabs=net70 ");
		Test("https://learn.microsoft.com/en-us/dotnet/core/install/windows?tabs=net70#install-with-windows-installer");
		Test(@"C:\Windows\System32\aadtb.dll", "file:///C:/Windows/System32/aadtb.dll");
		Test(
			"https://learn.microsoft.com/en-us/dotnet/core/install/windows (install-with-windows-installer)",
			"https://learn.microsoft.com/en-us/dotnet/core/install/windows",
			"(install-with-windows-installer)");

		static void Test(string text, string? uri = null, string? comment = null)
		{
			uri ??= text;
			LineContext context = LineContextTests.Create(text);
			UriLine line = UriLine.TryParse(context).ShouldNotBeNull(text);
			line.Uri.ShouldBe(new Uri(uri));
			if (comment is not null)
			{
				line.Annotations.Count.ShouldBe(1);
				line.Annotations[0].ShouldBeOfType<Comment>().ToString().ShouldBe(comment);
				if (text.EndsWith(comment))
				{
					text = text.Substring(0, text.Length - comment.Length);
				}
			}

			line.Text.ShouldBe(text.Trim());
		}
	}

	[TestMethod]
	public void TryParseInvalidTest()
	{
		Test("Not a Uri");
		Test("[A#]");
		Test("Mesa/Boogie");

		static void Test(string text)
		{
			LineContext context = LineContextTests.Create(text);
			UriLine? line = UriLine.TryParse(context);
			line.ShouldBeNull();
		}
	}
}