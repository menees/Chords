using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>A detached saved-tab draft. An absent grouping means a flat filtered list.</summary>
/// <param name="Id">The existing tab identity, or null for a new tab.</param>
/// <param name="Name">The user-facing tab name.</param>
/// <param name="Search">The complete saved search expression.</param>
/// <param name="GroupBy">An optional catalog grouping field.</param>
public sealed record CustomTabEdit(Guid? Id, string Name, string Search, string? GroupBy)
{
	/// <summary>Gets whether the draft has a name and a supported optional grouping.</summary>
	public bool IsValid => !string.IsNullOrWhiteSpace(this.Name) && (this.GroupBy is null || BookGroupingIndex.Fields.ContainsKey(this.GroupBy));

	/// <summary>Starts new tabs from the current search and edits from the complete saved definition.</summary>
	public static CustomTabEdit Create(string currentSearch, CustomTabCatalogItem? existing = null)
	{
		ArgumentNullException.ThrowIfNull(currentSearch);
		if (existing is { IsSupported: false })
		{
			throw new InvalidOperationException("This tab uses an unsupported definition. Create a new tab instead.");
		}

		return existing is null ? new(null, string.Empty, currentSearch, null)
			: new(existing.Id, existing.Name, existing.Search, existing.GroupBy);
	}
}