namespace Menees.Chords.Db;

/// <summary>Represents one custom-tab sort criterion.</summary>
public sealed class SortSpecification
{
	/// <summary>Gets or sets the field name.</summary>
	public string Field { get; set; } = string.Empty;

	/// <summary>Gets or sets whether sorting is descending.</summary>
	public bool Descending { get; set; }
}
