namespace Menees.Chords.Db;

/// <summary>Represents a user-defined song-browser tab.</summary>
public sealed class CustomTab
{
	/// <summary>Gets or sets the tab identifier.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets the tab name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the filter tree.</summary>
	public FilterNode? Filter { get; set; }

	/// <summary>Gets or sets the grouping field.</summary>
	public string? GroupBy { get; set; }

	/// <summary>Gets or sets the sort specifications.</summary>
	public List<SortSpecification> Sort { get; set; } = [];

	/// <summary>Gets or sets the revision stamp.</summary>
	public RevisionStamp Revision { get; set; } = new();
}
