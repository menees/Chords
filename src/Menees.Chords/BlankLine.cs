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

	private BlankLine()
		: base(string.Empty)
	{
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the shared blank line instance.
	/// </summary>
	public static BlankLine Instance { get; } = new();

	#endregion
}
