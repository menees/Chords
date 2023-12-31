﻿namespace Menees.Chords;

#region Using Directives

using System.IO;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A set of "name ######"  or "name #-#-#-#-#-#" chord fingering definitions.
/// </summary>
/// <seealso href="https://www.chordpro.org/chordpro/directives-define/"/>
/// <seealso href="https://www.ultimate-guitar.com/contribution/help/rubric#iii3"/>
public sealed class ChordDefinitions : Entry
{
	#region Constructors

	internal ChordDefinitions(IReadOnlyList<ChordDefinition> definitions, IEnumerable<Entry>? annotations = null)
		: base(annotations)
	{
		this.Definitions = definitions;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the chord definitions for this entry.
	/// </summary>
	public IReadOnlyList<ChordDefinition> Definitions { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as sequence of chord definitions.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance of the line was parsed as a sequence of chord definitions. Null otherwise.</returns>
	public static ChordDefinitions? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);

		ChordDefinitions? result = annotations.OfType<ChordDefinitions>().FirstOrDefault();
		if (result != null)
		{
			// If there's something else on the line, then this isn't just a chord definition line.
			if (lexer.Read(skipLeadingWhiteSpace: true))
			{
				result = null;
			}
			else if (annotations.Count > 1)
			{
				result.AddAnnotations(annotations.Where(entry => entry != result));
			}
		}

		return result;
	}

	#endregion

	#region Protected Methods

	/// <summary>
	/// Writes the formatted text of the chord definitions line.
	/// </summary>
	/// <param name="writer">Used to write the output.</param>
	protected override void WriteWithoutAnnotations(TextWriter writer)
		=> WriteJoin(writer, this.Definitions, w => w.Write(", "), (w, definition) => w.Write(definition));

	#endregion
}
