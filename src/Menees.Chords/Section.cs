namespace Menees.Chords;

#region Using Directives

using System.Collections.Generic;
using System.IO;
using System.Linq;

#endregion

/// <summary>
/// An explicit or implicit group of <see cref="Entry"/>s within a <see cref="Document"/>.
/// </summary>
public sealed class Section : Entry, IEntryContainer
{
	#region Constructors

	/// <summary>
	/// Creates a new section with the specified entries.
	/// </summary>
	/// <param name="entries">The values to include in <see cref="Entries"/>.</param>
	public Section(IEnumerable<Entry> entries)
	{
		Conditions.RequireNonEmpty(entries);
		this.Entries = entries.ToList();
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the ordered collection of entries within the current section.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	#endregion

	#region Public Methods

	/// <inheritdoc/>
	public override void Write(TextWriter writer, bool includeAnnotations)
		=> WriteJoin(writer, this.Entries, (w, entry) => entry.Write(w, includeAnnotations));

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override void WriteWithoutAnnotations(TextWriter writer)
		=> this.Write(writer, false);

	#endregion
}
