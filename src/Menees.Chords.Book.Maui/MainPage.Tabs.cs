#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;

#endregion

namespace Menees.Chords.Book.Maui;

public partial class MainPage
{
	#region Private Data

	private const int FirstCustomTabIndex = 4;
	private IReadOnlyList<CustomTabCatalogItem> customTabs = [];

	#endregion

	#region Private Properties

	private CustomTabCatalogItem? CurrentCustomTab => this.ManagementTabs.SelectedIndex >= FirstCustomTabIndex
		? this.customTabs.ElementAtOrDefault(this.ManagementTabs.SelectedIndex - FirstCustomTabIndex) : null;

	private string CurrentViewKey => this.CurrentCustomTab is CustomTabCatalogItem tab ? tab.Id.ToString("D")
		: this.ManagementTabs.SelectedIndex == RecentTabIndex ? "Recent" : this.ManagementTabs.SelectedIndex == ArtistsTabIndex ? "Artists" : "Songs";

	private SongViewSort? CurrentViewSort => this.session.Database is null ? null : this.session.GetViewSort(this.CurrentViewKey)
		?? (this.CurrentCustomTab is { IsSupported: true } tab ? new(tab.SortBy, tab.SortDescending) : null);

	private string? SongGrouping => this.ManagementTabs.SelectedIndex == ArtistsTabIndex ? "artist"
		: this.CurrentCustomTab is CustomTabCatalogItem tab ? tab.GroupBy
		: this.CurrentViewSort is null or { Field: "title", Descending: false } ? "title" : null;

	#endregion

	#region Private Methods

	private void RefreshCustomTabs(Guid? selectedId = null)
	{
		this.customTabs = this.session.GetCustomTabs();
		string[] titles = ["Songs", "Setlists", "Recent", "Artists", .. this.customTabs.Select(tab => tab.Name)];
		if (!this.ManagementTabs.Titles.SequenceEqual(titles, StringComparer.Ordinal))
		{
			this.ManagementTabs.Titles = titles;
		}

		if (selectedId is Guid id)
		{
			int index = this.customTabs.ToList().FindIndex(tab => tab.Id == id);
			this.ManagementTabs.SelectedIndex = index < 0 ? 0 : FirstCustomTabIndex + index;
		}
		else if (this.ManagementTabs.SelectedIndex >= titles.Length)
		{
			this.ManagementTabs.SelectedIndex = 0;
		}
	}

	private async void HandleViewActionsClicked(object? sender, EventArgs e)
		=> await this.RunBookMutationAsync(async () =>
		{
			CustomTabCatalogItem? current = this.CurrentCustomTab;
			string[] choices = current is null ? ["Sort View…", "New Tab…", "Search Help"]
				: current.IsSupported ? ["Sort View…", "New Tab…", "Edit Tab…", "Delete This Tab", "Search Help"]
				: ["New Tab…", "Delete This Tab", "Search Help"];
			string? action = await this.DisplayActionSheetAsync("Song Views", "Cancel", null, choices).ConfigureAwait(true);
			if (action == "Sort View…")
			{
				await this.ChangeViewSortAsync().ConfigureAwait(true);
			}
			else if (action == "Search Help")
			{
				const string Help = "Combine words or quoted phrases with fields, for example:\nartist:\"Joni Mitchell\" key:c genre:folk\n\n"
						+ "Fields: title, artist, tag, key, genre, album, time (exact time signature), capo, duration (seconds or m:ss).\n\n"
						+ "Exclude a field value with !, for example tag:!LyricOnly. Songs without that field also match.\n"
						+ "Wildcards: * matches any number of characters; ? matches one. Match a whole field value: tag:Christ* or artist:\"*Jones\".\n"
						+ "tag:* finds songs with a tag; tag:!* finds songs without tags. Plain text keeps substring matching.\n"
						+ "Use backslash for literal special characters: tag:\\!Important, tag:\\*, tag:what\\?, \\\\ for a backslash, \\\" for a quote.\n"
						+ "Double quotes group spaces; wildcards still work inside quotes. Square brackets are literal.\n\n"
						+ "Genre comes from source metadata such as {meta: genre Folk}; it is separate from tags.\n\n"
						+ "Use true or false with: archived, display, metronome, multiple, recovery, archivedfiles.\n"
						+ "Enable Show Archived to include archived songs. All terms must match. Save a search as a tab from Views.";
				await this.DisplayAlertAsync("Search", Help, "Close").ConfigureAwait(true);
			}
			else if (action == "Delete This Tab" && current is not null)
			{
				bool confirmed = await this.DisplayAlertAsync("Delete saved tab?", $"Delete {current.Name}? Songs are retained.", "Delete Tab", "Cancel")
					.ConfigureAwait(true);
				if (confirmed)
				{
					await this.session.DeleteCustomTabAsync(current.Id).ConfigureAwait(true);
					this.ManagementTabs.SelectedIndex = 0;
					this.RefreshCustomTabs();
				}
			}
			else if (action is "New Tab…" or "Edit Tab…")
			{
				CustomTabEdit initial = CustomTabEdit.Create(this.SongSearch.Text ?? string.Empty, action == "Edit Tab…" ? current : null);
				CustomTabEdit? saved = null;
				Guid? id = await WindowsSavedTabDialog.ShowAsync(this, initial, async (draft, token) =>
				{
					Guid result = await this.session.SaveCustomTabAsync(draft, token).ConfigureAwait(true);
					saved = draft;
					return result;
				}).ConfigureAwait(true);
				if (id is Guid selectedId && saved is not null)
				{
					this.session.SaveViewSort(selectedId.ToString("D"), new());
					this.RefreshCustomTabs(selectedId);
					this.SongSearch.Text = saved.Search;
					this.ApplyFilter(saved.Search);
				}
			}
		}).ConfigureAwait(true);

	private async Task ChangeViewSortAsync()
	{
		SongViewSort? sort = await WindowsSongViewSortDialog.ShowAsync(this, this.session.GetViewSort(this.CurrentViewKey) ?? new())
			.ConfigureAwait(true);
		if (sort is not null)
		{
			this.session.SaveViewSort(this.CurrentViewKey, sort);
			this.ApplyFilter(this.SongSearch.Text);
		}
	}

	#endregion
}
