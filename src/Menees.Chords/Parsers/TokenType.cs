namespace Menees.Chords.Parsers;

/// <summary>
/// The supported <see cref="Token"/> types.
/// </summary>
public enum TokenType
{
	/// <summary>
	/// No token was matched (e.g., from the default(Token) struct).
	/// </summary>
	None,

	/// <summary>
	/// The token contains only characters where <see cref="char.IsWhiteSpace(char)"/> is true.
	/// </summary>
	WhiteSpace,

	/// <summary>
	/// The token text is surrounded by square brackets (e.g., [C#]);
	/// </summary>
	/// <remarks>
	/// The text inside the brackets may be empty (e.g., token is []) or whitespace (e.g., token is [ ]).
	/// </remarks>
	Bracketed,

	/// <summary>
	/// The token is general-purpose text (e.g., lyrics).
	/// </summary>
	Text,
}
