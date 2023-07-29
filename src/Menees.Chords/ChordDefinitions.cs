namespace Menees.Chords;

/// <summary>
/// A set of "name ######"  or "name #-#-#-#-#-#" chord fingering definitions.
/// </summary>
/// <seealso href="https://www.chordpro.org/chordpro/directives-define/"/>
/// <seealso href="https://www.ultimate-guitar.com/contribution/help/rubric#iii3"/>
public sealed class ChordDefinitions : Entry
{
	#region Public Methods

	// Name = xxxxxx [separator repeat]. Use define: https://www.chordpro.org/chordpro/directives-define
	// Allow '_' in place of 'x'. See Hey There Delilah doc.
	// UG also suggests '-' for high frets: Cmaj7 x-x-10-12-12-12
	// https://www.ultimate-guitar.com/contribution/help/rubric#iii3
	// TODO: Parse. [Bill, 7/21/2023]

	/// <summary>
	/// Gets the text of the original chord definitions line.
	/// </summary>
	public override string ToString() => "TODO"; // TODO: Finish ChordDefinitions.ToString. [Bill, 7/22/2023]

	#endregion
}
