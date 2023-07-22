namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.Linq;

#endregion

/// <summary>
/// An explicit or implicit group of <see cref="Entry"/>s within a <see cref="Document"/>.
/// </summary>
public sealed class Section : Entry
{
	#region Constructors

	/// <summary>
	/// Creates a new section with the specified entries.
	/// </summary>
	/// <param name="entries">The values to include in <see cref="Entries"/>.</param>
	public Section(IEnumerable<Entry> entries)
	{
		this.Entries = entries.ToList();
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the entries within the current section.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Concatenates the section's <see cref="Entries"/> using <see cref="Environment.NewLine"/> separators.
	/// </summary>
	public override string ToString() => string.Join(Environment.NewLine, this.Entries);

	#endregion
}
