namespace Menees.Chords.Formatters;

#region Using Directives

using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;

#endregion

/// <summary>
/// Formats an <see cref="IEntryContainer"/> as flat or indented text.
/// </summary>
public sealed class TextFormatter : ContainerFormatter
{
	#region Private Data Members

	private readonly string indent;
	private string? text;
	private IndentedTextWriter? writer;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="container">The container to format.</param>
	/// <param name="indent">The text to use for each level of indentation. This can be null/empty,
	/// a tab character, multiple spaces, or a custom pattern like "--->".</param>
	public TextFormatter(IEntryContainer container, string? indent = null)
		: base(container)
	{
		this.indent = indent ?? string.Empty;
	}

	#endregion

	#region Public Methods

	/// <inheritdoc/>
	public override string ToString()
	{
		if (this.text is null)
		{
			this.Format();
		}

		return this.text ?? string.Empty;
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override void Format(Entry entry, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireReference(this.writer);
		this.writer.WriteLine(entry);
	}

	/// <inheritdoc/>
	protected override void BeginContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.BeginContainer(container, hierarchy);

		// StringWriter's Dispose does nothing of consequence (just sets a bool).
		this.writer ??= new(new StringWriter(), this.indent);
		this.writer.Indent = hierarchy.Count;
	}

	/// <inheritdoc/>
	protected override void EndContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireReference(this.writer);
		base.EndContainer(container, hierarchy);

		this.writer.Indent = hierarchy.Count;
		if (hierarchy.Count == 0)
		{
			this.text = this.writer.InnerWriter.ToString();
		}
	}

	#endregion
}
