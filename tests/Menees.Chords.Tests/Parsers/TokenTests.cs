﻿namespace Menees.Chords.Parsers;

[TestClass]
public class TokenTests
{
	[TestMethod]
	public void ConstructorDefaultsTest()
	{
		// Other tests ensure the constructor works when all args are specified.
		// So, we'll just check the defaulted arg logic here.
		new Token(string.Empty).Type.ShouldBe(TokenType.None);
		new Token("\t").Type.ShouldBe(TokenType.WhiteSpace);
		new Token("[A]").Type.ShouldBe(TokenType.Text); // Not Bracketed; see constructor comments.
		new Token("x").Type.ShouldBe(TokenType.Text);
		new Token(" mix [text] ").Type.ShouldBe(TokenType.Text);

		new Token("x").Index.ShouldBe(0);
	}

	[TestMethod]
	public void PropertiesTest()
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

	[TestMethod]
	public void OperatorsTest()
	{
		// The default token has a null this.text member, and here we pass in "".
		Token empty = new(string.Empty, TokenType.None, 0);
		empty.ShouldNotBe(default);

		Token a1 = new("A", TokenType.Text, 1);
		Token a2 = new("A", TokenType.Text, 1);
		a1.ShouldBe(a2);
		a2.ShouldBe(a1);
		(a1 == a2).ShouldBeTrue();
		(a2 == a1).ShouldBeTrue();
		Token a3 = new("A", TokenType.Text, 3);
		a1.ShouldNotBe(a3);
		a3.ShouldNotBe(a1);
		(a1 != a3).ShouldBeTrue();
		(a3 != a1).ShouldBeTrue();
		a3.ShouldNotBe(new Token("A", TokenType.Bracketed, 3));
		a3.ShouldNotBe(new Token("B", TokenType.Text, 3));

		Dictionary<Token, int> dictionary = new() { [a1] = 1, [a3] = 3, };
		dictionary.TryGetValue(a2, out int value).ShouldBeTrue();
		value.ShouldBe(1);
	}

	[TestMethod]
	public void ToStringTest()
	{
		default(Token).ToString().ShouldBe(string.Empty);
		new Token("A").ToString().ShouldBe("A");
		new Token("\t").ToString().ShouldBe("\t");

		new Token("[B]").ToString().ShouldBe("[B]"); // TokenType.Text
		new Token("[B]", TokenType.Bracketed).ToString().ShouldBe("[[B]]");
		new Token("B", TokenType.Bracketed).ToString().ShouldBe("[B]");
	}
}
