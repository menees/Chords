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
	/// <param name="annotations">A collection of optional end-of-line annotations.</param>
	public Section(IEnumerable<Entry> entries, IEnumerable<Entry>? annotations = null)
		: base(annotations)
	{
		Conditions.RequireNonEmpty(entries);
		this.Entries = [.. entries];
		this.Environment = ChordProEnvironment.TryCreate(this.Entries);
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the ordered collection of entries within the current section.
	/// </summary>
	public IReadOnlyList<Entry> Entries { get; }

	#endregion

	#region Internal Properties

	/// <summary>
	/// Gets the ChordPro environment represented by this section, or null for non-environment sections.
	/// </summary>
	internal ChordProEnvironment? Environment { get; }

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
