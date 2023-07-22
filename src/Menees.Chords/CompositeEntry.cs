namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;

#endregion

/// <summary>
/// Used to group multiple related entries into a single entry.
/// </summary>
/// <remarks>
/// This is useful for scenarios like <see cref="ChordLyricPair"/>,
/// for when a <see cref="HeaderLine"/> also contains a <see cref="Comment"/>,
/// and for when <see cref="ChordDefinitions"/> are at the end of line.
/// </remarks>
public class CompositeEntry : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new instance for the specified <paramref name="entries"/>.
	/// </summary>
	/// <param name="entries">The contained entries in order.</param>
	public CompositeEntry(params Entry[] entries)
	{
		this.Entries = entries;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the ordered entries contained within this composite entry.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	#endregion
}
