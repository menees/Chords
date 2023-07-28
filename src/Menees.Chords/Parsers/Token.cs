namespace Menees.Chords.Parsers;

#region Using Directives

using System.Diagnostics.CodeAnalysis;

#endregion

/// <summary>
/// A single token returned by a <see cref="Lexer"/> when splitting an input line.
/// </summary>
public readonly struct Token : IEquatable<Token>
{
	#region Private Data Members

	// Use a private field so the property accessor can ensure a non-null return value
	// even for default instances (e.g., from default(Token) or in new Token[n] arrays).
	private readonly string text;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The text value of the token.</param>
	/// <param name="type">The type of the token's <paramref name="text"/>.</param>
	/// <param name="index">The index where <paramref name="text"/> started in the input line.</param>
	public Token(string text, TokenType type, int index)
	{
		this.text = text;
		this.Type = type;
		this.Index = index;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// The text value of the token.
	/// </summary>
	public string Text => this.text ?? string.Empty;

	/// <summary>
	/// The type of the token's <see cref="Text"/>.
	/// </summary>
	public TokenType Type { get; }

	/// <summary>
	/// The 0-based index where <see cref="Text"/> started in the input line.
	/// </summary>
	public int Index { get; }

	#endregion

	#region Public Operators

	/// <summary>
	/// Implements the == operator.
	/// </summary>
	public static bool operator ==(Token left, Token right) => left.Equals(right);

	/// <summary>
	/// Implements the != operator.
	/// </summary>
	public static bool operator !=(Token left, Token right) => !left.Equals(right);

	#endregion

	#region Public Methods

	/// <inheritdoc/>
	public bool Equals(Token other)
		=> string.Equals(this.text, other.text)
		&& this.Type == other.Type
		&& this.Index == other.Index;

	/// <inheritdoc/>
	public override bool Equals([NotNullWhen(true)] object? obj)
		=> obj is Token token && this.Equals(token);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> unchecked(this.Text.GetHashCode() + this.Type.GetHashCode() + this.Index);

	/// <inheritdoc/>
	public override string ToString()
		=> $"{this.Text} ({this.Type}) @ {this.Index}";

	#endregion
}
