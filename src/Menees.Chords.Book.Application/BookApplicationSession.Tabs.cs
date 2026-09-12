using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

public sealed partial class BookApplicationSession
{
	public IReadOnlyList<CustomTabCatalogItem> GetCustomTabs()
		=> [.. (this.Database ?? throw new InvalidOperationException("No book is open.")).CustomTabs
			.OrderBy(tab => tab.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(tab => tab.Id)
			.Select(tab => new CustomTabCatalogItem(
				tab.Id,
				tab.Name,
				tab.Filter?.Value ?? string.Empty,
				tab.GroupBy,
				IsSupported(tab)))];

	public async Task<Guid> SaveCustomTabAsync(
		Guid? id, string name, string search, string? groupBy, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(search);
		if (groupBy is not null and not "title" and not "artist")
		{
			throw new ArgumentException("Group by title or artist.", nameof(groupBy));
		}

		Guid savedId = id ?? Guid.CreateVersion7();
		await this.MutateMetadataAsync(
			(database, now) =>
			{
				CustomTab tab;
				if (id is null)
				{
					tab = new() { Id = savedId };
					database.CustomTabs.Add(tab);
				}
				else
				{
					tab = database.CustomTabs.Single(item => item.Id == savedId);
				}

				tab.Name = name.Trim();
				tab.Filter = new() { Operator = "search", Value = search };
				tab.GroupBy = groupBy;
				tab.Sort = [new() { Field = "title" }];
				tab.Revision = NextRevision(tab.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken).ConfigureAwait(false);
		return savedId;
	}

	public Task DeleteCustomTabAsync(Guid id, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				CustomTab tab = database.CustomTabs.Single(item => item.Id == id);
				database.CustomTabs.Remove(tab);
				AddTombstone(database, tab.Id, nameof(CustomTab), tab.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);

	private static bool IsSupported(CustomTab tab)
		=> (tab.Filter is null || (tab.Filter.Operator == "search" && tab.Filter.Field is null && tab.Filter.Children.Count == 0))
			&& tab.GroupBy is null or "title" or "artist"
			&& tab.Sort.All(sort => sort.Field == "title" && !sort.Descending);
}
