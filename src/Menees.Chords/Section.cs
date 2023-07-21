namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.Linq;

#endregion

/// <summary>
/// Represents an explicit or implicit group of <see cref="Entry"/>s within a <see cref="Document"/>.
/// </summary>
public sealed class Section
{
	#region Constructors

	internal Section(List<Entry> entries)
	{
		this.Entries = entries;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the section header/name (if any) from the first entry in <see cref="Entries"/>.
	/// </summary>
	public string? Header => (this.Entries.FirstOrDefault() as LabeledEntry)?.Label;

	/// <summary>
	/// Gets the entries within the current section.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	#endregion
}
