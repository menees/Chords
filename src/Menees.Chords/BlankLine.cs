namespace Menees.Chords;

/// <summary>
/// A blank line in a document.
/// </summary>
/// <remarks>
/// This may be useful when grouping entries into <see cref="Section"/>s.
/// </remarks>
public sealed class BlankLine : TextEntry
{
	#region Constructors

	internal BlankLine()
		: base(string.Empty)
	{
	}

	#endregion
}
