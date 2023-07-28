﻿namespace Menees.Chords.Parsers;

[TestClass]
public class LexerTests
{
	[TestMethod]
	public void Read()
	{
		Test(string.Empty);
		Test("   ", new Token("   ", TokenType.WhiteSpace, 0));
		Test("[Xyz]", new Token("Xyz", TokenType.Bracketed, 0));
		Test("Abc", new Token("Abc", TokenType.Text, 0));

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
			List<Token> actual = new();
			Lexer lexer = new(text);
			while (lexer.Read())
			{
				actual.Add(lexer.Token);
			}

			lexer.Token.Type.ShouldBe(TokenType.None);

			actual.Count.ShouldBe(expected.Length);
			for (int i = 0; i < actual.Count; i++)
			{
				Token actualToken = actual[i];
				Token expectedToken = expected[i];
				actualToken.Text.ShouldBe(expectedToken.Text);
				actualToken.Type.ShouldBe(expectedToken.Type);
				actualToken.Index.ShouldBe(expectedToken.Index);
			}
		}
	}
}