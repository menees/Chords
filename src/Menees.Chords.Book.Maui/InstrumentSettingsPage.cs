#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;
using Microsoft.Maui.Layouts;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class InstrumentSettingsPage : ContentPage
{
	#region Private Data

	private const int InstrumentChoiceWidth = 190;
	private const int MaximumSemitones = 24;
	private const int PagePadding = 16;
	private const int ControlSpacing = 8;
	private readonly BookSession session;
	private readonly Guid? songId;
	private readonly SetlistEntrySettings? entry;
	private readonly Dictionary<Guid, CheckBox> assignments = [];
	private readonly Dictionary<Guid, SongInstrumentSnapshot> snapshots = [];
	private readonly Dictionary<Guid, SongInstrumentOptions> pendingOptions = [];
	private readonly VerticalStackLayout songOptions;
	private readonly FlexLayout assignmentRows = new() { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap };
	private readonly Guid bookId;
	private readonly long settingsRevision;
	private readonly Picker profiles = new() { Title = "Instrument" };
	private readonly Picker files = new() { Title = "Preferred sheet" };
	private readonly Picker spelling = new() { Title = "Chord spelling", ItemsSource = Enum.GetNames<AccidentalPreference>() };
	private readonly Picker behavior = new() { Title = "Capo behavior", ItemsSource = new[] { "Display only", "Show chord shapes" } };
	private readonly Microsoft.Maui.Controls.Entry transpose = new() { Keyboard = Keyboard.Default, Placeholder = "Transpose (-24 to +24)" };
	private readonly Microsoft.Maui.Controls.Entry capo = new() { Keyboard = Keyboard.Numeric, Placeholder = "Capo (blank or 0–24)" };
	private readonly Label status = new();
	private readonly TaskCompletionSource<bool> completion = new();
	private InstrumentProfileInfo[] instruments = [];
	private SongFileCatalogItem[] sheets = [];
	private SongInstrumentSnapshot? original;
	private bool loadingProfiles;
	private bool busy;
	private bool changed;

	#endregion

	#region Constructors

	public InstrumentSettingsPage(BookSession session, Guid? songId = null, SetlistEntrySettings? entry = null)
	{
		this.session = session;
		this.songId = songId;
		this.entry = entry;
		this.bookId = session.Database!.Id;
		this.settingsRevision = session.Database.BookSettings.Revision.Revision;
		this.Title = songId.HasValue ? "Song Instruments" : "Instruments";
		this.profiles.SelectedIndexChanged += (_, _) => this.LoadSongOptions();
		FluentIconButton create = new() { Icon = "Add", Description = "New Instrument…" };
		create.Clicked += async (_, _) => await this.EditProfileAsync(true).ConfigureAwait(true);
		FluentIconButton rename = new() { Icon = "Rename", Description = "Rename…" };
		rename.Clicked += async (_, _) => await this.EditProfileAsync(false).ConfigureAwait(true);
		FluentIconButton delete = new() { Icon = "Delete", Description = "Delete…" };
		delete.Clicked += async (_, _) => await this.DeleteProfileAsync().ConfigureAwait(true);
		FluentIconButton save = new() { Icon = "Save", Description = "Save" };
		save.Clicked += async (_, _) => await this.SaveAsync(false).ConfigureAwait(true);
		FluentIconButton reset = new() { Icon = "ArrowReset", Description = "Reset Song Settings", IsVisible = songId.HasValue };
		reset.Clicked += async (_, _) => await this.SaveAsync(true).ConfigureAwait(true);
		FluentIconButton close = new() { Icon = "Dismiss", Description = "Cancel" };
		close.Clicked += async (_, _) => await this.CloseAsync().ConfigureAwait(true);
		this.songOptions = new()
		{
			IsVisible = songId.HasValue,
			Spacing = ControlSpacing,
			Children =
			{
				new Label { Text = "Transpose changes the sounding key. In chord-shape mode, capo is subtracted from the shown chords. Text sheets only." },
				new Label { Text = "Transpose semitones (-24 to +24)" }, this.transpose,
				new Label { Text = "Capo fret (blank or 0–24)" }, this.capo, this.behavior, this.spelling, this.files,
			},
		};
		ScrollView body = new()
		{
			Content = new VerticalStackLayout
			{
				Padding = PagePadding,
				Spacing = ControlSpacing,
				Children =
				{
					new Label
					{
						Text = songId.HasValue ? "Assign the instruments used by this song. Choose an instrument below to edit its settings."
						: "Choose the default instrument for this book.",
					},
					this.assignmentRows, this.profiles,
					this.songOptions, this.status,
				},
			},
		};
		this.Content = DialogLayout.Create(this.Title, body, save, close, reset, create, rename, delete);
		this.RefreshProfiles(entry?.InstrumentProfileId ?? session.Database.BookSettings.DefaultInstrumentId);
	}

	#endregion

	#region Public Properties

	public Task<bool> Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		_ = this.CloseAsync();
		return true;
	}

	#endregion

	#region Private Methods

	private void RefreshProfiles(Guid? selected)
	{
		this.loadingProfiles = true;
		this.original = null;
		Dictionary<Guid, bool> assignedStates = this.assignments.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked);
		this.instruments = [.. this.session.Instruments.GetProfiles()];
		this.assignmentRows.Children.Clear();
		this.assignments.Clear();
		foreach (Guid deleted in this.snapshots.Keys.Except(this.instruments.Select(item => item.Id)).ToArray())
		{
			this.snapshots.Remove(deleted);
			this.pendingOptions.Remove(deleted);
		}

		if (this.songId is Guid song)
		{
			foreach (InstrumentProfileInfo instrument in this.instruments)
			{
				SongInstrumentSnapshot snapshot = this.snapshots.TryGetValue(instrument.Id, out var previous)
					? previous : this.session.Instruments.GetSongSettings(song, instrument.Id);
				CheckBox assigned = new() { IsChecked = assignedStates.GetValueOrDefault(instrument.Id, snapshot.SettingId.HasValue) };
				assigned.CheckedChanged += (_, _) => this.UpdateOptionsEnabled();
				SemanticProperties.SetDescription(assigned, "Assign " + instrument.Name);
				this.assignments.Add(instrument.Id, assigned);
				this.snapshots[instrument.Id] = snapshot;
				this.pendingOptions.TryAdd(instrument.Id, snapshot.Options);
				this.assignmentRows.Children.Add(new HorizontalStackLayout
				{
					Spacing = 0,
					WidthRequest = InstrumentChoiceWidth,
					Children = { assigned, new Label { Text = instrument.Name, VerticalOptions = LayoutOptions.Center } },
				});
			}
		}

		this.profiles.ItemsSource = this.instruments.Select(item => item.Name).ToArray();
		int selectedIndex = selected.HasValue ? Array.FindIndex(this.instruments, item => item.Id == selected.Value) : -1;
		this.profiles.SelectedIndex = this.instruments.Length > 0 ? Math.Max(0, selectedIndex) : -1;
		this.loadingProfiles = false;
		this.LoadSongOptions();
	}

	private InstrumentProfileInfo? GetSelected()
		=> this.profiles.SelectedIndex >= 0 && this.profiles.SelectedIndex < this.instruments.Length
			? this.instruments[this.profiles.SelectedIndex] : null;

	private void LoadSongOptions()
	{
		if (!this.loadingProfiles)
		{
			try
			{
				this.CaptureOptions();
				this.LoadSelectedOptions();
			}
			catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
			{
				this.status.Text = exception.Message;
				this.loadingProfiles = true;
				this.profiles.SelectedIndex = Array.FindIndex(this.instruments, item => item.Id == this.original?.InstrumentId);
				this.loadingProfiles = false;
			}
		}
	}

	private void LoadSelectedOptions()
	{
		this.original = this.songId is Guid song && this.GetSelected() is InstrumentProfileInfo profile
			? this.snapshots[profile.Id] : null;
		SongInstrumentOptions options = this.original is null ? new() : this.pendingOptions[this.original.InstrumentId];
		this.transpose.Text = options.TransposeSemitones.ToString(CultureInfo.CurrentCulture);
		this.capo.Text = options.CapoFret?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
		this.behavior.SelectedIndex = (int)options.CapoBehavior;
		this.spelling.SelectedIndex = (int)options.Spelling;
		this.sheets = this.songId is Guid id ? [.. this.session.GetSongFiles(id).Where(file => !file.IsArchived && !file.IsRecoveryVersion)] : [];
		this.files.ItemsSource = new List<string> { "Default sheet" }.Concat(this.sheets.Select(file => file.Name)).ToArray();
		this.files.SelectedIndex = options.PreferredSongFileId is Guid fileId ? 1 + Array.FindIndex(this.sheets, file => file.Id == fileId) : 0;
		this.UpdateOptionsEnabled();
	}

	private void UpdateOptionsEnabled()
		=> this.songOptions.IsEnabled = this.original is not null && this.assignments[this.original.InstrumentId].IsChecked;

	private void CaptureOptions()
	{
		if (this.original is SongInstrumentSnapshot snapshot)
		{
			SongInstrumentOptions options = new(
				int.Parse(this.transpose.Text, CultureInfo.CurrentCulture),
				string.IsNullOrWhiteSpace(this.capo.Text) ? null : int.Parse(this.capo.Text, CultureInfo.CurrentCulture),
				(CapoBehavior)this.behavior.SelectedIndex,
				(AccidentalPreference)this.spelling.SelectedIndex,
				this.files.SelectedIndex > 0 ? this.sheets[this.files.SelectedIndex - 1].Id : null);
			if (options.TransposeSemitones is < -MaximumSemitones or > MaximumSemitones || options.CapoFret is < 0 or > MaximumSemitones)
			{
				throw new ArgumentException("Transpose must be -24 to +24 and capo must be blank or 0–24.");
			}

			this.pendingOptions[snapshot.InstrumentId] = options;
		}
	}

	private async Task EditProfileAsync(bool create)
		=> await this.RunAsync(async () =>
		{
			this.CaptureOptions();
			InstrumentProfileInfo? originalProfile = create ? null : this.GetSelected();
			if (create || originalProfile is not null)
			{
				string? name = await this.DisplayPromptAsync(
					"Instrument name",
					"For example: Guitar, Piano, Bass or Vocal.",
					initialValue: originalProfile?.Name ?? string.Empty).ConfigureAwait(true);
				if (name is not null)
				{
					Guid id = await Task.Run(() => this.session.Instruments.SaveProfileAsync(originalProfile, name, this.session.DeviceId))
						.ConfigureAwait(true);
					this.changed = true;
					this.RefreshProfiles(id);
				}
			}
		}).ConfigureAwait(true);

	private async Task DeleteProfileAsync()
		=> await this.RunAsync(async () =>
		{
			this.CaptureOptions();
			InstrumentProfileInfo? profile = this.GetSelected();
			if (profile is not null && await this.DisplayAlertAsync("Delete instrument?", profile.Name, "Delete", "Cancel").ConfigureAwait(true))
			{
				await Task.Run(() => this.session.Instruments.DeleteProfileAsync(profile, this.session.DeviceId)).ConfigureAwait(true);
				this.changed = true;
				this.RefreshProfiles(null);
			}
		}).ConfigureAwait(true);

	private async Task SaveAsync(bool reset)
		=> await this.RunAsync(async () =>
		{
			InstrumentProfileInfo? profile = this.GetSelected();
			if (this.songId.HasValue)
			{
				if (this.original is null)
				{
					throw new InvalidOperationException("Choose or create an instrument first.");
				}

				SongInstrumentSnapshot original = this.original;
				if (reset)
				{
					this.pendingOptions[original.InstrumentId] = new();
				}
				else
				{
					this.CaptureOptions();
				}

				SetlistEntrySettings? clear = null;
				if (this.entry?.TransposeSemitones is not null
					&& (this.entry.InstrumentProfileId is null || this.entry.InstrumentProfileId == profile?.Id)
					&& await this.DisplayAlertAsync(
						"Clear this entry's transpose?",
						"Use these song-for-instrument settings for this setlist entry too? Its separate transpose override will be cleared.",
						"Save and Clear Entry",
						"Keep Entry Override").ConfigureAwait(true))
				{
					clear = this.entry;
				}

				var updates = this.snapshots.Values
					.Where(item => this.assignments[item.InstrumentId].IsChecked != item.SettingId.HasValue
						|| this.pendingOptions[item.InstrumentId] != item.Options || (clear is not null && item.InstrumentId == original.InstrumentId))
					.Select(item => (item, this.assignments[item.InstrumentId].IsChecked
						? this.pendingOptions[item.InstrumentId] : null)).ToArray();
				if (updates.Length > 0)
				{
					await Task.Run(() => this.session.Instruments.SaveSongSettingsBatchAsync(
						updates, this.session.DeviceId, clear, clearEntryInstrumentId: original.InstrumentId)).ConfigureAwait(true);
				}
			}
			else
			{
				if (profile is null)
				{
					throw new InvalidOperationException("Choose a default instrument.");
				}

				await Task.Run(() => this.session.Instruments.SaveDefaultAsync(
					this.bookId, this.settingsRevision, profile.Id, this.session.DeviceId)).ConfigureAwait(true);
			}

			this.session.ActiveInstrumentId = null;
			this.changed = true;
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(true);
		}).ConfigureAwait(true);

	private async Task RunAsync(Func<Task> operation)
	{
		if (!this.busy)
		{
			this.busy = true;
			try
			{
				await operation().ConfigureAwait(true);
			}
			catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException
				or OverflowException or IOException or UnauthorizedAccessException)
			{
				this.status.Text = exception.Message;
			}
			finally
			{
				this.busy = false;
			}
		}
	}

	private async Task CloseAsync()
	{
		if (!this.busy)
		{
			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult(this.changed);
		}
	}

	#endregion
}
