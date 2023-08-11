namespace Menees.Chords.Formatters;

#region Using Directives

using System.Collections.Generic;
using System.Text;

#endregion

/// <summary>
/// Formats an <see cref="IEntryContainer"/> as flat or indented text.
/// </summary>
public sealed class TextFormatter : ContainerFormatter
{
	#region Private Data Members

	private readonly string? indent;

	// Start at -1 so we'll be at level 0 when we begin the root container.
	private int level = -1;
	private string? text;
	private StringBuilder? builder;

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
		this.indent = indent;
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
		Conditions.RequireReference(this.builder);

		Indent();

		string text = entry.ToString();
		foreach (char ch in text)
		{
			this.builder.Append(ch);

			// If a formatted entry spans multiple lines, then we need to indent subsequent lines too.
			// Inspired by Hans: https://stackoverflow.com/a/2547800/1882616
			if (ch == '\n')
			{
				Indent();
			}
		}

		this.builder.AppendLine();

		void Indent()
		{
			if (!string.IsNullOrEmpty(this.indent))
			{
				for (int i = 0; i < this.level; i++)
				{
					this.builder.Append(this.indent);
				}
			}
		}
	}

	/// <inheritdoc/>
	protected override void BeginContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.BeginContainer(container, hierarchy);

		this.builder ??= new();
		this.level++;
	}

	/// <inheritdoc/>
	protected override void EndContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireReference(this.builder);
		base.EndContainer(container, hierarchy);

		this.level--;

		if (hierarchy.Count == 0)
		{
			this.text = this.builder.ToString();
		}
	}

	#endregion
}
