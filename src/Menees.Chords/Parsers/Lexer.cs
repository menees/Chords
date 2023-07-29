namespace Menees.Chords.Parsers;

#region Private Data Members

using System;
using System.Text;

#endregion

/// <summary>
/// Splits a line of text into <see cref="Token"/>s.
/// </summary>
public sealed class Lexer
{
	#region Private Data Members

	private readonly string text;
	private int index;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance to tokenize <see cref="text"/>.
	/// </summary>
	/// <param name="text">The text to split into tokens.</param>
	public Lexer(string text)
	{
		this.text = text ?? string.Empty;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the current token
	/// </summary>
	/// <remarks>
	/// This will be a default/empty token before <see cref="Read()"/> is called
	/// and after it returns false.
	/// </remarks>
	public Token Token { get; private set; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to read the next <see cref="Token"/> from the text.
	/// </summary>
	/// <returns>True if a new token was read. False if there are no more tokens to read.</returns>
	public bool Read()
	{
		// Instead of "default", start with a None token at the current index.
		this.Token = new(string.Empty, TokenType.None, this.index);

		int tokenIndex = this.index;
		char? ch = this.GetChar();
		if (ch != null)
		{
			if (char.IsWhiteSpace(ch.Value))
			{
				this.Token = this.ReadWhiteSpace(ch, tokenIndex);
			}
			else if (ch.Value == '[')
			{
				this.Token = this.ReadBracketed(tokenIndex);
			}
			else
			{
				this.Token = this.ReadText(ch, tokenIndex);
			}
		}

		return ch != null;
	}

	/// <summary>
	/// Tries to read the next <see cref="Token"/> from the text and optionally skips a leading
	/// <see cref="TokenType.WhiteSpace"/> token.
	/// </summary>
	/// <param name="skipLeadingWhiteSpace">Whether to skip a leading whitespace token.</param>
	/// <returns>True if a new token was read. False if there are no more tokens to read.</returns>
	public bool Read(bool skipLeadingWhiteSpace)
	{
		bool result = this.Read();
		if (result && skipLeadingWhiteSpace && this.Token.Type == TokenType.WhiteSpace)
		{
			result = this.Read();
		}

		return result;
	}

	/// <summary>
	/// Reads all text from the current position (i.e., including <see cref="Token"/>) to the end of the text
	/// and returns it as one string, optionally ignoring trailing whitespace.
	/// </summary>
	/// <param name="skipTrailingWhiteSpace">Whether to omit text from the last token if it has
	/// <see cref="TokenType.WhiteSpace"/>.</param>
	/// <returns>A string that contains all characters from the current position to the end of the
	/// text optionally excluding trailing whitespace.</returns>
	public string ReadToEnd(bool skipTrailingWhiteSpace = false)
	{
		StringBuilder sb = new(this.text.Length);
		int? lastWhiteSpaceStartIndex;
		do
		{
			lastWhiteSpaceStartIndex = this.Token.Type == TokenType.WhiteSpace ? sb.Length : null;

			// Use Token.ToString() so brackets are included when needed.
			sb.Append(this.Token.ToString());
		}
		while (this.Read());

		if (skipTrailingWhiteSpace && lastWhiteSpaceStartIndex != null)
		{
			sb.Length = lastWhiteSpaceStartIndex.Value;
		}

		string result = sb.ToString();
		return result;
	}

	/// <summary>
	/// Moves the lexer back to the start of the text.
	/// </summary>
	public void Reset()
	{
		this.index = 0;
		this.Token = default;
	}

	#endregion

	#region Private Methods

	private char? GetChar()
	{
		char? result = this.PeekChar();

		if (result != null)
		{
			this.index++;
		}

		return result;
	}

	private void UngetChar()
	{
		if (this.index > 0)
		{
			this.index--;
		}
	}

	private char? PeekChar()
		=> this.index < this.text.Length ? this.text[this.index] : null;

	private Token ReadWhiteSpace(char? ch, int tokenIndex)
	{
		StringBuilder sb = new();
		sb.Append(ch);

		while ((ch = this.GetChar()) != null)
		{
			if (char.IsWhiteSpace(ch.Value))
			{
				sb.Append(ch.Value);
			}
			else
			{
				this.UngetChar();
				break;
			}
		}

		string text = sb.ToString();
		return new Token(text, TokenType.WhiteSpace, tokenIndex);
	}

	private Token ReadBracketed(int tokenIndex)
	{
		// We're not including the brackets because the caller would
		// typically need to remove them (e.g., to parse a chord name).
		// This is similar to a C or SQL lexer that returns a string with no
		// outer quotes and with any escaped characters unescaped.
		StringBuilder sb = new();

		// If we can't find an end bracket, then the token type willl be Text.
		TokenType type = TokenType.Text;
		char? ch;
		while ((ch = this.GetChar()) != null)
		{
			// We don't support any escape syntax for embedded end brackets.
			if (ch.Value == ']')
			{
				type = TokenType.Bracketed;
				break;
			}
			else
			{
				sb.Append(ch.Value);
			}
		}

		string text = sb.ToString();
		return new Token(text, type, tokenIndex);
	}

	private Token ReadText(char? ch, int tokenIndex)
	{
		StringBuilder sb = new();
		sb.Append(ch);

		while ((ch = this.GetChar()) != null)
		{
			if (ch == '[' || char.IsWhiteSpace(ch.Value))
			{
				this.UngetChar();
				break;
			}
			else
			{
				sb.Append(ch.Value);
			}
		}

		string text = sb.ToString();
		return new(text, TokenType.Text, tokenIndex);
	}

	#endregion
}
