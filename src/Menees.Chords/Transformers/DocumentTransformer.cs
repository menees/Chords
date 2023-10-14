namespace Menees.Chords.Transformers;

#region Using Directives

using Menees.Chords.Parsers;

#endregion

/// <summary>
/// Use to create new <see cref="Document"/> instances by transforming
/// the entries from an initial <see cref="Document"/>.
/// </summary>
public abstract class DocumentTransformer
{
	#region Constructors

	/// <summary>
	/// Creates a new instance to transform the specified <paramref name="document"/>.
	/// </summary>
	/// <param name="document">The document to transform.</param>
	/// <remarks>
	/// Since each <see cref="Document"/> instance is immutable, each transform
	/// creates a new document instance.
	/// </remarks>
	protected DocumentTransformer(Document document)
	{
		Conditions.RequireNonNull(document);
		this.Document = document;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the document being transformed in its current state.
	/// </summary>
	public Document Document { get; private set; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Recursively flattens all <see cref="IEntryContainer"/> entries
	/// so that all <see cref="Entry"/>s are in the returned list.
	/// </summary>
	/// <param name="entries">The entries list to flatten.</param>
	/// <param name="includeAnnotations">Whether <see cref="Entry.Annotations"/> should be included
	/// by cloning each <see cref="Entry"/> to remove its annotations and then adding them as top-level
	/// entries after their original parent entry.</param>
	/// <returns>A flattened list of entries.</returns>
	public static IReadOnlyList<Entry> Flatten(IReadOnlyList<Entry> entries, bool includeAnnotations = false)
	{
		Conditions.RequireNonNull(entries);
		List<Entry> result = new(entries.Count);
		Flatten(entries, includeAnnotations, result);
		return result;
	}

	/// <summary>
	/// Clones <see cref="Document"/> and recursively flattens all <see cref="IEntryContainer"/> entries
	/// so that all <see cref="Entry"/>s are in the new <see cref="Document.Entries"/> list.
	/// </summary>
	/// <param name="includeAnnotations">Whether <see cref="Entry.Annotations"/> should be included
	/// by cloning each <see cref="Entry"/> to remove its annotations and then adding them as top-level
	/// entries after their original parent entry.</param>
	/// <returns>The current transformer.</returns>
	public DocumentTransformer Flatten(bool includeAnnotations = false)
		=> this.SetEntries(Flatten(this.Document.Entries, includeAnnotations));

	/// <summary>
	/// Clones <see cref="Document"/> and changes its <see cref="Document.Entries"/> to <paramref name="entries"/>.
	/// </summary>
	/// <param name="entries">The new entries to use.</param>
	/// <returns>The current transformer.</returns>
	public DocumentTransformer SetEntries(IReadOnlyList<Entry> entries)
	{
		Conditions.RequireNonNull(entries);
		this.Document = new(entries, this.Document.FileName);
		return this;
	}

	/// <summary>
	/// Clones <see cref="Document"/> and changes its <see cref="Document.FileName"/> to <paramref name="fileName"/>.
	/// </summary>
	/// <param name="fileName">The new file name to use.</param>
	/// <returns>The current transformer.</returns>
	public DocumentTransformer SetFileName(string? fileName)
	{
		this.Document = new(this.Document.Entries, fileName);
		return this;
	}

	/// <summary>
	/// Transforms the <see cref="Document"/> entries or filename into another format.
	/// </summary>
	/// <returns>The current transformer.</returns>
	public abstract DocumentTransformer Transform();

	#endregion

	#region Protected Methods

	/// <summary>
	/// Gets <see cref="Document.Entries"/> and groups them using <see cref="DocumentParser"/>'s
	/// default groupers if they're not grouped already.
	/// </summary>
	protected IReadOnlyList<Entry> GetGroupedEntries()
	{
		IReadOnlyList<Entry> entries = this.Document.Entries;
		if (!entries.OfType<IEntryContainer>().Any())
		{
			DocumentParser parser = new();
			entries = parser.GroupEntries(entries);
		}

		return entries;
	}

	#endregion

	#region Private Methods

	private static void Flatten(IReadOnlyList<Entry> input, bool includeAnnotations, List<Entry> output)
	{
		foreach (Entry entry in input)
		{
			Entry outputEntry = entry;
			IReadOnlyList<Entry>? annotations = null;
			if (includeAnnotations)
			{
				annotations = entry.Annotations;
				outputEntry = entry.Clone(null);
			}

			if (outputEntry is IEntryContainer container)
			{
				Flatten(container.Entries, includeAnnotations, output);
			}
			else
			{
				output.Add(outputEntry);
			}

			if (annotations != null)
			{
				Flatten(annotations, includeAnnotations, output);
			}
		}
	}

	#endregion
}
