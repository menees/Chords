namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;

#endregion

/// <summary>
/// One or more related, parsed lines from a <see cref="Document"/>.
/// </summary>
public abstract class Entry
{
	#region Private Data Members

	private List<Entry>? annotations;

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
	public IReadOnlyList<Entry> Annotations => this.annotations ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();

	#endregion

	#region Public Methods

	/// <summary>
	/// Gets the text representation of the current entry.
	/// </summary>
	public abstract override string ToString();

	#endregion

	#region Protected Methods

	/// <summary>
	/// Adds a sub-entry that's on the same line as the current entry.
	/// </summary>
	/// <param name="annotation">The sub-entry to add.</param>
	protected void AddAnnotation(Entry annotation)
	{
		this.annotations ??= new();
		this.annotations.Add(annotation);
	}

	#endregion
}
