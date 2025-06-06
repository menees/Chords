﻿namespace Menees.Chords;

#region Using Directives

using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line of lyrics only (or otherwise unmatched text).
/// </summary>
/// <remarks>
/// A lyric line is not in ChordPro format, so it won't contain embedded [Chord] tokens.
/// </remarks>
public sealed class LyricLine : TextEntry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified text.
	/// </summary>
	/// <param name="text">The lyrics or text for this line.</param>
	/// <param name="annotations">A collection of optional end-of-line annotations.</param>
	public LyricLine(string text, IEnumerable<Entry>? annotations = null)
		: base(text, annotations)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns the current context line as a new text line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance.</returns>
	public static LyricLine Parse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
		string line = lexer.ReadToEnd(skipTrailingWhiteSpace: annotations.Count == 0);

		// If a lyric line ends with comments in parentheses, then they're probably repeated harmonies
		// instead of comments about the lines. For example, in Hotel California:
		//     "Such a lovely place (Such a lovely place)"
		// The harmony part might even be the whole line, e.g., in Mrs. Robinson:
		//     "(Hey, hey, hey...hey, hey, hey)"
		// If there's enough whitespace between the parenthetical comment and the lyric text,
		// then it MIGHT be an annotation Comment entry. For example, in Simple Man:
		//     "And be a simple kind of man                              (only in acoustic version)"
		// However, it could still be a harmony following a long instrumental gap like:
		//     F                             C        G
		//     Now I must say more than ever            (come on Eileen)
		// Since we can't tell unambiguously, we'll treat trailing comments as part of the lyric line.
		StringBuilder? sb = null;
		int index = 0;
		while (annotations.Count > index
			&& annotations[index] is Comment comment
			&& comment.Prefix == "("
			&& comment.Suffix == ")")
		{
			sb ??= new(line);
			sb.Append(comment);
			if (++index < annotations.Count)
			{
				sb.Append(' ');
			}
		}

		if (sb != null && index > 0)
		{
			line = sb.ToString();
			annotations = [.. annotations.Skip(index)];
		}

		LyricLine result = new(line);
		result.AddAnnotations(annotations);
		return result;
	}

	#endregion
}
