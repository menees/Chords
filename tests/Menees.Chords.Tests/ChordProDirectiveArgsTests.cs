namespace Menees.Chords;

[TestClass]
public class ChordProDirectiveArgsTests
{
	#region Public Methods

	[TestMethod]
	public void ToStringTest()
	{
		Test(null);
		Test(string.Empty);
		Test("Yamato");
		Test("The Beatles");
		Test("src='myfile.jpg' width=\"30\"", "src", "myfile.jpg", "width", "30");
		Test("width=\"30\" src='myfile.jpg'", "width", "30", "src", "myfile.jpg");
		Test(@"label='Line 1\nLine2'", "label", "Line 1\nLine2");

		static void Test(string? text, params string[] expectedAttributes)
		{
			var args = Create(text);
			string? expectedText = string.IsNullOrWhiteSpace(text) ? null : text;
			args.Value.ShouldBe(expectedText);
			args.ToString().ShouldBe(expectedText);

			if (args.Attributes.Count == 0)
			{
				args.FirstValue.ShouldBe(args.Value);
			}
			else
			{
				args.FirstValue.ShouldBe(args.Attributes.First().Value);
			}

			args.Attributes.Count.ShouldBe(expectedAttributes.Length / 2);
			for (int index = 0; index < args.Attributes.Count; index++)
			{
				int offset = 2 * index;
				string key = expectedAttributes[offset];
				string value = expectedAttributes[offset + 1];
				args.Attributes[key].ShouldBe(value);
			}
		}
	}

	#endregion

	#region Private Methods

	private static ChordProDirectiveArgs Create(string? argument)
		=> ChordProDirectiveLineTests.Parse($"{{test {argument}}}").Args;

	#endregion
}