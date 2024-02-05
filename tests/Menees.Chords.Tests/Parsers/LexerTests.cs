namespace Menees.Chords.Parsers;

[TestClass]
public class LexerTests
{
	#region Public Methods

	[TestMethod]
	public void ReadTest()
	{
		Test(string.Empty);
		Test("   ", new Token("   ", TokenType.WhiteSpace, 0));
		Test("[Xyz]", new Token("Xyz", TokenType.Bracketed, 0));
		Test("Abc", new Token("Abc"));

		Test(
			"[C] N.C.",
			new Token("C", TokenType.Bracketed, 0),
			new Token(" ", TokenType.WhiteSpace, 3),
			new Token("N.C.", TokenType.Text, 4));

		Test(
			"  A Bb [stop] C# ",
			new Token("  ", TokenType.WhiteSpace, 0),
			new Token("A", TokenType.Text, 2),
			new Token(" ", TokenType.WhiteSpace, 3),
			new Token("Bb", TokenType.Text, 4),
			new Token(" ", TokenType.WhiteSpace, 6),
			new Token("stop", TokenType.Bracketed, 7),
			new Token(" ", TokenType.WhiteSpace, 13),
			new Token("C#", TokenType.Text, 14),
			new Token(" ", TokenType.WhiteSpace, 16));

		static void Test(string text, params Token[] expected)
		{
			List<Token> actual = [];
			Lexer lexer = new(text);
			lexer.Token.ShouldBe(default);

			while (lexer.Read())
			{
				actual.Add(lexer.Token);
			}

			lexer.Token.ShouldBe(new Token(string.Empty, TokenType.None, text.Length));

			actual.Count.ShouldBe(expected.Length);
			for (int i = 0; i < actual.Count; i++)
			{
				Token actualToken = actual[i];
				Token expectedToken = expected[i];
				actualToken.ShouldBe(expectedToken);
			}
		}
	}

	[TestMethod]
	public void ReadSkipLeadingWhiteSpaceTest()
	{
		Lexer lexer = new("Test");
		lexer.Read(skipLeadingWhiteSpace: true).ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("Test"));
		lexer.Reset();
		lexer.Read(skipLeadingWhiteSpace: false).ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("Test"));

		lexer = new("  Test  ");
		lexer.Read(skipLeadingWhiteSpace: true).ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("Test", TokenType.Text, 2));
		lexer.Reset();
		lexer.Read(skipLeadingWhiteSpace: false).ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("  ", TokenType.WhiteSpace, 0));
	}

	[TestMethod]
	public void ReadToEndTest()
	{
		Lexer lexer = new("  A [B] C  ");
		lexer.ReadToEnd().ShouldBe("  A [B] C  ");

		lexer.Reset();
		lexer.Read(skipLeadingWhiteSpace: true).ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("A", TokenType.Text, 2));
		lexer.ReadToEnd(skipTrailingWhiteSpace: true).ShouldBe("A [B] C");

		lexer.Reset();
		lexer.Read(skipLeadingWhiteSpace: true).ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("A", TokenType.Text, 2));
		lexer.ReadToEnd(skipTrailingWhiteSpace: false).ShouldBe("A [B] C  ");
	}

	[TestMethod]
	public void ResetTest()
	{
		Lexer lexer = new("abc");
		lexer.Read().ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("abc"));

		lexer.Reset();
		lexer.Token.ShouldBe(default);

		lexer.Read().ShouldBeTrue();
		lexer.Token.ShouldBe(new Token("abc"));
	}

	#endregion
}