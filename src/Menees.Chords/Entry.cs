namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;

#endregion

/// <summary>
/// Represents one or more related, parsed lines from a <see cref="Document"/>.
/// </summary>
public abstract class Entry
{
	#region Private Data Members

	private List<Entry>? inlines;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	protected Entry()
	{
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the ordered sub-entries contained on the same line with this entry.
	/// </summary>
	/// <remarks>
	/// This is useful for entries like a <see cref="HeaderLine"/> that also contains a <see cref="Comment"/>,
	/// and for when <see cref="ChordDefinitions"/> are at the end of <see cref="ChordLine"/>.
	/// </remarks>
	public IReadOnlyList<Entry> Inlines => this.inlines ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();

	#endregion

	#region Protected Methods

	/// <summary>
	/// Adds a sub-entry that's on the same line as the current entry.
	/// </summary>
	/// <param name="inline">The sub-entry to add.</param>
	protected void AddInline(Entry inline)
	{
		this.inlines ??= new();
		this.inlines.Add(inline);
	}

	#endregion
}
