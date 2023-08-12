namespace Menees.Chords;

#region Using Directives

using System.IO;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A comment line or a comment segment on the same line as another entry.
/// </summary>
public sealed class Comment : TextEntry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified comment.
	/// </summary>
	/// <param name="text">The comment text with no prefix or suffix.</param>
	/// <param name="prefix">An optional prefix that was before <paramref name="text"/>.</param>
	/// <param name="suffix">An optional suffix that was after <paramref name="text"/>.</param>
	public Comment(string text, string? prefix = null, string? suffix = null)
		: base(text)
	{
		this.Prefix = prefix;
		this.Suffix = suffix;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the optional prefix that was before <see cref="TextEntry.Text"/>.
	/// </summary>
	public string? Prefix { get; }

	/// <summary>
	/// Gets the optional suffix that was before <see cref="TextEntry.Text"/>.
	/// </summary>
	public string? Suffix { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a comment.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new comment if the line starts with "#", "*", or is surrounded by parentheses.</returns>
	public static Comment? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		Comment? result = null;

		Lexer lexer = context.CreateLexer();
		if (lexer.Read(skipLeadingWhiteSpace: true) && lexer.Token.Type == TokenType.Text)
		{
			if (lexer.Token.Text[0] == '#')
			{
				// A comment line can end with a '#', (e.g., F#).
				string line = lexer.ReadToEnd(skipTrailingWhiteSpace: true);
				string trimmed = line.TrimStart('#').TrimStart(' ');
				string prefix = line.Substring(0, line.Length - trimmed.Length);
				result = new(trimmed, prefix);
			}
			else if (lexer.Token.Text[0] == '*')
			{
				string line = lexer.ReadToEnd(skipTrailingWhiteSpace: true);
				result = Split(line, '*', '*');
			}
			else if (lexer.Token.Text[0] == '(')
			{
				string line = lexer.ReadToEnd(skipTrailingWhiteSpace: true);
				if (line[^1] == ')')
				{
					result = Split(line, '(', ')');
				}
			}
		}

		return result;

		static Comment Split(string line, char start, char end)
		{
			Comment? result;
			string startTrimmed = line.TrimStart(start).TrimStart(' ');
			string prefix = line.Substring(0, line.Length - startTrimmed.Length);
			string text = startTrimmed.TrimEnd(end).TrimEnd();
			string suffix = startTrimmed.Substring(text.Length);
			result = new(text, prefix, suffix);
			return result;
		}
	}

	#endregion

	#region Protected Methods

	/// <summary>
	/// Writes the formatted comment text including <see cref="Prefix"/> and <see cref="Suffix"/>.
	/// </summary>
	/// <param name="writer">Used to write the output.</param>
	protected override void WriteWithoutAnnotations(TextWriter writer)
	{
		Conditions.RequireNonNull(writer);
		writer.Write(this.Prefix);
		base.WriteWithoutAnnotations(writer);
		writer.Write(this.Suffix);
	}

	#endregion
}
