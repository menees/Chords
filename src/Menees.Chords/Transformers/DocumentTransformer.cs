namespace Menees.Chords.Transformers;

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
	/// <returns>A flattened list of entries.</returns>
	public static IReadOnlyList<Entry> Flatten(IReadOnlyList<Entry> entries)
	{
		HashSet<Entry> visited = new();
		List<Entry> result = new(entries.Count);
		Flatten(entries, visited, result);
		return result;

		// TODO: Flatten annotations too? [Bill, 8/7/2023]
	}

	/// <summary>
	/// Clones <see cref="Document"/> and recursively flattens all <see cref="IEntryContainer"/> entries
	/// so that all <see cref="Entry"/>s are in the new <see cref="Document.Entries"/> list.
	/// </summary>
	/// <returns>The current transformer.</returns>
	public DocumentTransformer Flatten()
		=> this.SetEntries(Flatten(this.Document.Entries));

	/// <summary>
	/// Clones <see cref="Document"/> and changes its <see cref="Document.Entries"/> to <paramref name="entries"/>.
	/// </summary>
	/// <param name="entries">The new entries to use.</param>
	/// <returns>The current transformer.</returns>
	public DocumentTransformer SetEntries(IReadOnlyList<Entry> entries)
	{
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

	#endregion

	#region Private Methods

	private static void Flatten(IReadOnlyList<Entry> input, HashSet<Entry> visited, List<Entry> output)
	{
		foreach (Entry entry in input)
		{
			if (!visited.Contains(entry))
			{
				visited.Add(entry);
				if (entry is IEntryContainer container)
				{
					Flatten(container.Entries, visited, output);
				}
				else
				{
					output.Add(entry);
				}
			}
		}
	}

	#endregion
}
