namespace Menees.Chords.Db;

/// <summary>Represents a node in a custom-tab filter tree.</summary>
public sealed class FilterNode
{
	/// <summary>Gets or sets the filter operator.</summary>
	public string Operator { get; set; } = string.Empty;

	/// <summary>Gets or sets the field name.</summary>
	public string? Field { get; set; }

	/// <summary>Gets or sets the comparison value.</summary>
	public string? Value { get; set; }

	/// <summary>Gets or sets the child filter nodes.</summary>
	public List<FilterNode> Children { get; set; } = [];
}
