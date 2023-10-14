namespace Menees.Chords.Formatters;

#region Using Directives

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

#endregion

/// <summary>
/// Formats an <see cref="IEntryContainer"/> as XML.
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
	}

	#endregion

	#region Public Methods

	/// <inheritdoc/>
	public override string ToString()
	{
		this.EnsureRoot();
		return this.root.ToString();
	}

	/// <summary>
	/// Gets the formatted document as an XElement.
	/// </summary>
	public XElement ToXElement()
	{
		this.EnsureRoot();

		// Clone the root element so a caller can't mess with our private instance.
		XElement result = new(this.root);
		return result;
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override void Format(Entry entry, IReadOnlyCollection<IEntryContainer> hierarchy)
	{
		Conditions.RequireNonNull(this.currentContainer);
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
		string text = entry.ToString(includeAnnotations: false);
		if (!string.IsNullOrEmpty(text))
		{
			// We have to use CDATA because entries can contain significant whitespace.
			XCData data = new(text);

			// With no annotations, a single CDATA subnode is unambiguous. If there are annotations,
			// then their elements will have CDATA subnodes too, and we don't want XElement.Value
			// to merge them all together. So, if we have annotations, we'll use a <ToString> sub-element.
			XNode content = entry.Annotations.Count == 0 ? data : new XElement(nameof(ToString), data);
			element.Add(content);
		}

		foreach (Entry annotation in entry.Annotations)
		{
			AddEntry(annotation, element);
		}

		container.Add(element);
	}

	[MemberNotNull(nameof(this.root))]
	private void EnsureRoot()
	{
		if (this.root is null)
		{
			this.Format();
		}

		Conditions.RequireNonNull(this.root);
	}

	#endregion
}
