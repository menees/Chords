﻿namespace Menees.Chords;

#region Using Directives

using System.IO;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A "Ultimate Guitar"-style bracketed header (e.g., [Chorus], [Verse]).
/// </summary>
public sealed class HeaderLine : TextEntry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="text">The header label or section name.</param>
	/// <param name="annotations">A collection of optional end-of-line annotations.</param>
	internal HeaderLine(string text, IEnumerable<Entry>? annotations = null)
		: base(text, annotations)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse a header line from the current context.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if <paramref name="context"/>'s line was parsed. Null otherwise.</returns>
	public static HeaderLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		HeaderLine? result = null;

		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
		if (lexer.Read(skipLeadingWhiteSpace: true) && lexer.Token.Type == TokenType.Bracketed && !string.IsNullOrWhiteSpace(lexer.Token.Text))
		{
			// We have to pull the token text out here because we're about to move the lexer forward.
			string headerText = lexer.Token.Text;

			// Since annotations were removed earlier, make sure there's nothing else on the line.
			// And make sure the header text isn't a chord. We won't look for specific header values
			// since Ultimate Guitar has many header variations:
			// Intro, Outro, Verse, Verse #, Chorus, Interlude, Bridge, Pre-Chorus, Solo, Solo #, Break, Post-Chorus, Pre-Verse
			if ((!lexer.Read() || string.IsNullOrEmpty(lexer.ReadToEnd(skipTrailingWhiteSpace: true))) && !Chord.TryParse(headerText, out _))
			{
				result = new(headerText);
				result.AddAnnotations(annotations);
			}
		}

		return result;
	}

	#endregion

	#region Protected Methods

	/// <summary>
	/// Writes the header text in brackets.
	/// </summary>
	/// <param name="writer">Used to write output.</param>
	protected override void WriteWithoutAnnotations(TextWriter writer)
	{
		writer.Write('[');
		base.WriteWithoutAnnotations(writer);
		writer.Write(']');
	}

	#endregion
}
