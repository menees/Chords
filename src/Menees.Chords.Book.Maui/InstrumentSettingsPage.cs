#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class InstrumentSettingsPage : ContentPage
{
	#region Private Data

	private const int PagePadding = 16;
	private const int ControlSpacing = 8;
	private readonly BookSession session;
	private readonly Guid? songId;
	private readonly SetlistEntrySettings? entry;
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
	private bool busy;
	private bool changed;

	#endregion

	#region Constructors

	public InstrumentSettingsPage(BookSession session, Guid? songId = null, SetlistEntrySettings? entry = null)
	{
		this.session = session;
		this.songId = songId;
		this.entry = entry;
		this.Title = songId.HasValue ? "Song for Instrument" : "Instruments";
		this.profiles.SelectedIndexChanged += (_, _) => this.LoadSongOptions();
		Button create = new() { Text = "New Instrument…" };
		create.Clicked += async (_, _) => await this.EditProfileAsync(true).ConfigureAwait(true);
		Button rename = new() { Text = "Rename…" };
		rename.Clicked += async (_, _) => await this.EditProfileAsync(false).ConfigureAwait(true);
		Button delete = new() { Text = "Delete…" };
		delete.Clicked += async (_, _) => await this.DeleteProfileAsync().ConfigureAwait(true);
		FluentIconButton save = new() { Icon = "Save", Description = "Save" };
		save.Clicked += async (_, _) => await this.SaveAsync(false).ConfigureAwait(true);
		FluentIconButton reset = new() { Icon = "ArrowReset", Description = "Reset Song Settings", IsVisible = songId.HasValue };
		reset.Clicked += async (_, _) => await this.SaveAsync(true).ConfigureAwait(true);
		FluentIconButton close = new() { Icon = "Dismiss", Description = songId.HasValue ? "Cancel" : "Close" };
		close.Clicked += async (_, _) => await this.CloseAsync().ConfigureAwait(true);
		VerticalStackLayout options = new()
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
					new Label { Text = "Profiles and song settings belong to this book. The selected instrument is remembered on this device." },
					this.profiles, new HorizontalStackLayout { Spacing = ControlSpacing, Children = { create, rename, delete } },
					options, this.status,
				},
			},
		};
		this.Content = DialogLayout.Create(this.Title, body, save, close, reset);
		this.RefreshProfiles(entry?.InstrumentProfileId ?? session.ActiveInstrumentId);
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
		this.instruments = [.. this.session.Instruments.GetProfiles()];
		this.profiles.ItemsSource = new List<string> { "No instrument" }.Concat(this.instruments.Select(item => item.Name)).ToArray();
		this.profiles.SelectedIndex = selected.HasValue ? 1 + Array.FindIndex(this.instruments, item => item.Id == selected.Value) : 0;
		this.LoadSongOptions();
	}

	private InstrumentProfileInfo? GetSelected()
		=> this.profiles.SelectedIndex > 0 && this.profiles.SelectedIndex <= this.instruments.Length
			? this.instruments[this.profiles.SelectedIndex - 1] : null;

	private void LoadSongOptions()
	{
		this.original = this.songId is Guid song && this.GetSelected() is InstrumentProfileInfo profile
			? this.session.Instruments.GetSongSettings(song, profile.Id) : null;
		SongInstrumentOptions options = this.original?.Options ?? new();
		this.transpose.Text = options.TransposeSemitones.ToString(CultureInfo.CurrentCulture);
		this.capo.Text = options.CapoFret?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
		this.behavior.SelectedIndex = (int)options.CapoBehavior;
		this.spelling.SelectedIndex = (int)options.Spelling;
		this.sheets = this.songId is Guid id ? [.. this.session.GetSongFiles(id).Where(file => !file.IsArchived && !file.IsRecoveryVersion)] : [];
		this.files.ItemsSource = new List<string> { "Default sheet" }.Concat(this.sheets.Select(file => file.Name)).ToArray();
		this.files.SelectedIndex = options.PreferredSongFileId is Guid fileId ? 1 + Array.FindIndex(this.sheets, file => file.Id == fileId) : 0;
	}

	private async Task EditProfileAsync(bool create)
		=> await this.RunAsync(async () =>
		{
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
				SongInstrumentOptions? options = reset ? null : new(
					int.Parse(this.transpose.Text, CultureInfo.CurrentCulture),
					string.IsNullOrWhiteSpace(this.capo.Text) ? null : int.Parse(this.capo.Text, CultureInfo.CurrentCulture),
					(CapoBehavior)this.behavior.SelectedIndex,
					(AccidentalPreference)this.spelling.SelectedIndex,
					this.files.SelectedIndex > 0 ? this.sheets[this.files.SelectedIndex - 1].Id : null);
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

				await Task.Run(() => this.session.Instruments.SaveSongSettingsAsync(original, options, this.session.DeviceId, clear)).ConfigureAwait(true);
			}

			this.session.ActiveInstrumentId = profile?.Id;
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
