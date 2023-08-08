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
	/// and for when <see cref="ChordDefinitions"/> are at the end of a <see cref="ChordLine"/>.
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
	/// Makes a shallow copy of the current entry and sets the result's annotations.
	/// </summary>
	/// <param name="annotations">The annotations to assign to the result's <see cref="Annotations"/>.</param>
	/// <returns>A new shallow copied instance with <see cref="Annotations"/> set to <paramref name="annotations"/>.</returns>
	protected internal Entry Clone(IEnumerable<Entry>? annotations)
	{
		Entry result = (Entry)this.MemberwiseClone();
		result.annotations = annotations?.ToList();
		return result;
	}

	/// <summary>
	/// Makes a shallow copy of the current entry
	/// </summary>
	/// <returns>A new shallow copied instance</returns>
	protected Entry Clone()
		=> this.Clone(this.annotations);

	/// <summary>
	/// Adds a sub-entry that's on the same line as the current entry.
	/// </summary>
	/// <param name="annotation">The sub-entry to add.</param>
	protected void AddAnnotation(Entry annotation)
	{
		Conditions.RequireReference(annotation);
		this.annotations ??= new();
		this.annotations.Add(annotation);
	}

	/// <summary>
	/// Adds sub-entries that are on the same line as the current entry.
	/// </summary>
	/// <param name="annotations">The sub-entries to add.</param>
	protected void AddAnnotations(IEnumerable<Entry> annotations)
	{
		Conditions.RequireReference(annotations);
		this.annotations ??= new();
		this.annotations.AddRange(annotations);
	}

	#endregion
}
