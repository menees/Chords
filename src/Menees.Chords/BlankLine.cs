namespace Menees.Chords;

/// <summary>
/// A blank line in a document.
/// </summary>
/// <remarks>
/// This is useful when grouping entries into <see cref="Section"/>s.
/// </remarks>
public sealed class BlankLine : Entry
{
	#region Constructors

	internal BlankLine()
	{
	}

	#endregion

	#region Public Methods

	/// <summary>
	/// Returns <see cref="string.Empty"/>.
	/// </summary>
	public override string ToString() => string.Empty;

	#endregion
}
