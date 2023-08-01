﻿namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// An entry made up of <see cref="TextSegment"/>s.
/// </summary>
public abstract class SegmentedEntry : Entry
{
	#region Private Data Members

	// https://www.ultimate-guitar.com/contribution/help/rubric#iii3 (section C. "No chord")
	private static readonly ISet<string> PseudoChords = new HashSet<string>(ChordParser.Comparer) { "N.C.", "NC", "stop" };

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="segments">The segments that make up the current line.</param>
	protected SegmentedEntry(IReadOnlyList<TextSegment> segments)
	{
		this.Segments = segments;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the text segments that make up the current line.
	/// </summary>
	/// <remarks>
	/// This can include a mix of segment types (e.g., <see cref="ChordSegment"/>,
	/// <see cref="TextSegment"/>, <see cref="WhiteSpaceSegment"/>).
	/// </remarks>
	public IReadOnlyList<TextSegment> Segments { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Gets current line text.
	/// </summary>
	public override string ToString()
	{
		StringBuilder sb = new();
		foreach (TextSegment segment in this.Segments)
		{
			sb.Append(segment);
		}

		foreach (Entry annotation in this.Annotations)
		{
			sb.Append(annotation);
		}

		string result = sb.ToString();
		return result;
	}

	#endregion

	#region Protected Methods

	/// <summary>
	/// Tries to split the current line into segments.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <param name="getSegment">An optional lambda to handle unrecognized <see cref="TextSegment"/>s.
	/// If the lambda returns null for a text token (or the lambda is null), then no segments are returned.</param>
	/// <param name="annotations">Returns any annotation entries parsed off the end of the line.</param>
	/// <returns>The list of segments from the line. This can be empty if <paramref name="getSegment"/> returned null.</returns>
	protected static IReadOnlyList<TextSegment> TryGetSegments(
		LineContext context,
		Func<Token, TextSegment?>? getSegment,
		out IReadOnlyList<Entry> annotations)
	{
		List<TextSegment> result = new();

		Lexer lexer = context.CreateLexer(out annotations);
		while (lexer.Read())
		{
			if (lexer.Token.Type == TokenType.WhiteSpace)
			{
				result.Add(new WhiteSpaceSegment(lexer.Token.Text));
			}
			else if (lexer.Token.Text.All(ch => !char.IsLetter(ch))
				|| PseudoChords.Contains(lexer.Token.Text)
				|| (lexer.Token.Text[0] == '(' && lexer.Token.Text[^1] == ')'))
			{
				// Allow tokens with no letter (e.g., ~↑↓*), pseudo-chords, or annotations in parentheses.
				result.Add(new TextSegment(lexer.Token.ToString(), lexer.Token.Index));
			}
			else if (lexer.Token.Text[^1] == '*' && Chord.TryParse(lexer.Token.Text[0..^1], out Chord? chord))
			{
				// Allow chords to end with an asterisk since they probably relate to a comment or footnote later.
				result.Add(new ChordSegment(chord, lexer.Token.Index, lexer.Token.ToString()));
			}
			else if (Chord.TryParse(lexer.Token.Text, out chord))
			{
				result.Add(new ChordSegment(chord, lexer.Token.Index, lexer.Token.ToString()));
			}
			else
			{
				TextSegment? segment = getSegment?.Invoke(lexer.Token);
				if (segment != null)
				{
					result.Add(segment);
				}
				else
				{
					result.Clear();
					break;
				}
			}
		}

		return result;
	}

	#endregion
}
