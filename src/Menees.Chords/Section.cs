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

	/// <summary>
	/// Creates a new section with the specified entries and header.
	/// </summary>
	/// <param name="entries">The values to include in <see cref="Entries"/>.</param>
	/// <param name="header">Optional text to assign to <see cref="Header"/>.</param>
	public Section(IEnumerable<Entry> entries, string? header = null)
	{
		this.Entries = entries.ToList();
		this.Header = header;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the entries within the current section.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	/// <summary>
	/// Gets the section header/name (if any).
	/// </summary>
	public string? Header { get; }

	#endregion
}
