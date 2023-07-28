namespace Menees.Chords.Parsers;

[TestClass]
public class TokenTests
{
	[TestMethod]
	public void Properties()
	{
		Token token = default;
		token.Text.ShouldBe(string.Empty);
		token.Type.ShouldBe(TokenType.None);
		token.Index.ShouldBe(0);

		token = new("Test", TokenType.Text, 1);
		token.Text.ShouldBe("Test");
		token.Type.ShouldBe(TokenType.Text);
		token.Index.ShouldBe(1);
	}
}
