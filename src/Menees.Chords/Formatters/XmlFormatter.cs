namespace Menees.Chords.Formatters;

#region Using Directives

using System.Collections.Generic;
using System.Xml.Linq;

#endregion

/// <summary>
/// Formats an <see cref="IEntryContainer"/> as flat or indented text.
/// </summary>
public sealed class XmlFormatter : ContainerFormatter
{
	#region Private Data Members

	private XElement? root;
	private XElement? currentContainer;

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance.
	/// </summary>
	/// <param name="container">The container to format.</param>
	public XmlFormatter(IEntryContainer container)
		: base(container)
	{
		// TODO: Constructor arg for enum EntryContent { CData, Text, Attribute } [Bill, 8/10/2023]
		// TODO: Or take a lambda that converts Entry to XNode. [Bill, 8/10/2023]
	}

	#endregion

	#region Public Methods

	/// <inheritdoc/>
	public override string ToString()
	{
		if (this.root is null)
		{
			this.Format();
		}

		return this.root?.ToString() ?? string.Empty;
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override void Format(Entry entry, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireReference(this.currentContainer);
		AddEntry(entry, this.currentContainer);
	}

	/// <inheritdoc/>
	protected override void BeginContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.BeginContainer(container, hierarchy);

		XElement newElement = new(container.GetType().Name);
		if (this.currentContainer is null)
		{
			if (this.root is null)
			{
				this.root = newElement;
				if (container is Document document && !string.IsNullOrWhiteSpace(document.FileName))
				{
					this.root.SetAttributeValue("FileName", document.FileName);
				}
			}
		}
		else
		{
			this.currentContainer.Add(newElement);
		}

		this.currentContainer = newElement;
	}

	/// <inheritdoc/>
	protected override void EndContainer(IEntryContainer container, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		base.EndContainer(container, hierarchy);
		this.currentContainer = this.currentContainer?.Parent ?? this.root;
	}

	#endregion

	#region Private Methods

	private static void AddEntry(Entry entry, XElement container)
	{
		// Ideally, we'd deep serialize out the public properties, but that's complicated.
		// We'll keep this really simple for now.
		XElement element = new(entry.GetType().Name);
		string text = entry.ToString();
		if (!string.IsNullOrEmpty(text))
		{
			// TODO: Pulling element.Value concats nested CDatas. This needs a sub-element. [Bill, 8/10/2023]
			element.Add(new XCData(text));
		}

		foreach (Entry annotation in entry.Annotations)
		{
			AddEntry(annotation, element);
		}

		container.Add(element);
	}

	#endregion
}
