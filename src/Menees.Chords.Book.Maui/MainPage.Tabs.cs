#region Using Directives

using Menees.Chords.Book.Application;

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

	private bool GroupSongsByArtist => this.ManagementTabs.SelectedIndex == ArtistsTabIndex || this.CurrentCustomTab?.GroupBy == "artist";

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
			string[] choices = current is null ? ["Save as New Tab", "Search Help"]
				: ["Save as New Tab", "Update This Tab", "Delete This Tab", "Search Help"];
			string? action = await this.DisplayActionSheetAsync("Song Views", "Cancel", null, choices).ConfigureAwait(true);
			if (action == "Search Help")
			{
				const string Help = "Combine words or quoted phrases with fields, for example:\nartist:\"Joni Mitchell\" key:c genre:folk\n\n"
						+ "Fields: title, artist, tag, key, genre, capo, duration (seconds or m:ss).\n\n"
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
			else if (action is "Save as New Tab" or "Update This Tab")
			{
				string? name = await this.DisplayPromptAsync("Saved Tab", "Name this search view.", initialValue: current?.Name ?? "My Songs")
					.ConfigureAwait(true);
				if (!string.IsNullOrWhiteSpace(name))
				{
					string? grouping = await this.DisplayActionSheetAsync("Group songs by", "Cancel", null, "Title", "Artist").ConfigureAwait(true);
					if (grouping is "Title" or "Artist")
					{
						Guid id = await this.session.SaveCustomTabAsync(
							action == "Update This Tab" ? current?.Id : null,
							name,
							this.SongSearch.Text ?? string.Empty,
							grouping == "Artist" ? "artist" : "title").ConfigureAwait(true);
						this.RefreshCustomTabs(id);
						this.ApplyFilter(this.SongSearch.Text);
					}
				}
			}
		}).ConfigureAwait(true);

	#endregion
}
