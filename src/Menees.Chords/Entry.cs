namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
	/// <param name="annotations">A collection of optional end-of-line annotations.</param>
	protected Entry(IEnumerable<Entry>? annotations = null)
	{
		if (annotations != null)
		{
			this.AddAnnotations(annotations);
		}
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
	/// Gets the text representation of the current entry including its annotations.
	/// </summary>
	public sealed override string ToString() => this.ToString(true);

	/// <summary>
	/// Gets the text representation of the current entry optionally including annotations.
	/// </summary>
	/// <param name="includeAnnotations">Whether annotations should be appended at the end of the entry.</param>
	public string ToString(bool includeAnnotations)
	{
		using StringWriter writer = new();
		this.Write(writer, includeAnnotations);
		string result = writer.ToString();
		return result;
	}

	/// <summary>
	/// Renders the text representation of the current entry optionally including annotations.
	/// </summary>
	/// <param name="writer">Used to write the output.</param>
	/// <param name="includeAnnotations">Whether annotations should be appended at the end of the entry.</param>
	public virtual void Write(TextWriter writer, bool includeAnnotations)
	{
		Conditions.RequireNonNull(writer);

		if (!includeAnnotations || this.Annotations.Count == 0)
		{
			// This is the most common case, and it can use the passed in writer directly.
			this.WriteWithoutAnnotations(writer);
		}
		else
		{
			// We need to write the first part with a lookback writer so we can check if it ends with whitespace.
			// Some entry types throw away insignificant whitespace (e.g., ChordDefinitions), so we may need to
			// add some back to separate the annotations from the entry content.
			using LookbackWriter lookbackWriter = new(writer);
			this.WriteWithoutAnnotations(lookbackWriter);
			if (lookbackWriter.LastWrite is not null && !char.IsWhiteSpace(lookbackWriter.LastWrite.Value))
			{
				writer.Write(' ');
			}

			WriteJoin(writer, this.Annotations, w => w.Write(' '), (w, annotation) => annotation.Write(w, includeAnnotations));
		}
	}

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
	/// Writes a collection of <paramref name="items"/> to <paramref name="writer"/>
	/// with <see cref="TextWriter.WriteLine()"/> between each.
	/// </summary>
	/// <typeparam name="T">The type of item to write.</typeparam>
	/// <param name="writer">Used to write the output.</param>
	/// <param name="items">The collection of items to write.</param>
	/// <param name="writeItem">Called to write each item.</param>
	protected static void WriteJoin<T>(
		TextWriter writer,
		IEnumerable<T> items,
		Action<TextWriter, T> writeItem)
		=> WriteJoin(writer, items, w => w.WriteLine(), writeItem);

	/// <summary>
	/// Writes a collection of <paramref name="items"/> to <paramref name="writer"/>
	/// with a custom separator between each.
	/// </summary>
	/// <typeparam name="T">The type of item to write.</typeparam>
	/// <param name="writer">Used to write the output.</param>
	/// <param name="items">The collection of items to write.</param>
	/// <param name="writeSeparator">Called to write a separator between items.</param>
	/// <param name="writeItem">Called to write each item.</param>
	protected static void WriteJoin<T>(
		TextWriter writer,
		IEnumerable<T> items,
		Action<TextWriter> writeSeparator,
		Action<TextWriter, T> writeItem)
	{
		Conditions.RequireNonNull(writer);
		Conditions.RequireNonNull(items);
		Conditions.RequireNonNull(writeSeparator);
		Conditions.RequireNonNull(writeItem);

		bool first = true;
		foreach (T item in items)
		{
			if (!first)
			{
				writeSeparator(writer);
			}

			writeItem(writer, item);
			first = false;
		}
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
		Conditions.RequireNonNull(annotation);
		this.annotations ??= new();
		this.annotations.Add(annotation);
	}

	/// <summary>
	/// Adds sub-entries that are on the same line as the current entry.
	/// </summary>
	/// <param name="annotations">The sub-entries to add.</param>
	protected void AddAnnotations(IEnumerable<Entry> annotations)
	{
		Conditions.RequireNonNull(annotations);
		this.annotations ??= new();
		this.annotations.AddRange(annotations);
	}

	/// <summary>
	/// Renders the text representation of the current entry without annotations.
	/// </summary>
	/// <param name="writer">Used to write the output.</param>
	protected abstract void WriteWithoutAnnotations(TextWriter writer);

	#endregion

	#region Private Types

	private sealed class LookbackWriter : TextWriter
	{
		#region Private Data Members

		private readonly TextWriter writer;

		#endregion

		#region Constructors

		public LookbackWriter(TextWriter writer)
		{
			this.writer = writer;
		}

		#endregion

		#region Public Properties

		public char? LastWrite { get; private set; }

		#endregion

		#region Public Methods

		public override Encoding Encoding => this.writer.Encoding;

		public override void Write(char value)
		{
			this.writer.Write(value);
			this.LastWrite = value;
		}

		#endregion
	}

	#endregion
}
