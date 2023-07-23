namespace Menees.Chords;

#region Using Directives

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
	/// <param name="comment"></param>
	public Comment(string comment)
		: base(comment)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a comment.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new comment if the line starts with "# ", "##", "*", or is surrounded by parentheses.</returns>
	public static Comment? TryParse(LineContext context)
	{
		Comment? result = null;

		string line = context.LineText.Trim();
		if (!string.IsNullOrEmpty(line))
		{
			if (line.StartsWith("# ") || line.StartsWith("##"))
			{
				result = new(line[2..]);
			}
			else if (line[0] == '*')
			{
				result = new(line.Trim('*'));
			}
			else if (line[0] == '(' && line[^1] == ')')
			{
				result = new(line[1..^1]);
			}
		}

		return result;
	}

	#endregion
}
