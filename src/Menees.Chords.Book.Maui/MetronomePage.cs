#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Services;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class MetronomePage : ContentPage
{
	#region Private Data

	private readonly Guid? songId;
	private readonly BookSession session;
	private readonly IMetronomeEngine engine;
	private readonly IDispatcherTimer timer;
	private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private bool busy;

	#endregion

	#region Public API

	public MetronomePage(Guid? songId, BookSession session, IMetronomeEngine engine)
	{
		this.songId = songId;
		this.session = session;
		this.engine = engine;
		this.InitializeComponent();
		this.saveSettings.Text = songId.HasValue ? "Save for This Song" : "Save Book Defaults";
		this.resetSettings.IsVisible = songId.HasValue;
		this.beatUnit.ItemsSource = new[] { "2", "4", "8", "16" };
		this.LoadSettings();
		this.timer = this.Dispatcher.CreateTimer();
		this.timer.Interval = TimeSpan.FromMilliseconds(50);
		this.timer.Tick += (_, _) =>
		{
			this.toggle.Text = this.engine.IsRunning ? "Stop" : "Start";
			this.pulse.Text = this.visual.IsChecked && this.engine.IsRunning ? this.engine.CurrentBeat.ToString(CultureInfo.CurrentCulture) : "—";
		};
		this.timer.Start();
	}

	public Task Completion => this.completion.Task;

	#endregion

	#region Protected Methods

	protected override bool OnBackButtonPressed()
	{
		this.HandleClose(this, EventArgs.Empty);
		return true;
	}

	#endregion

	#region Private Methods

	private void LoadSettings()
	{
		MetronomeSettings settings = this.session.GetMetronomeSettings(this.songId);
		this.bpm.Text = settings.BeatsPerMinute.ToString(CultureInfo.CurrentCulture);
		this.beats.Text = settings.BeatsPerMeasure.ToString(CultureInfo.CurrentCulture);
		this.beatUnit.SelectedItem = settings.BeatUnit.ToString(CultureInfo.InvariantCulture);
		this.volume.Value = settings.Volume;
		this.audio.IsChecked = settings.AudioEnabled;
		this.visual.IsChecked = settings.VisualEnabled;
		this.accent.IsChecked = settings.AccentFirstBeat;
	}

	private MetronomeSettings ReadSettings()
	{
		if (!int.TryParse(this.bpm.Text, out int tempo) || !int.TryParse(this.beats.Text, out int meter))
		{
			throw new InvalidOperationException("Enter whole numbers for tempo and beats per measure.");
		}

		MetronomeSettings settings = new()
		{
			BeatsPerMinute = tempo, BeatsPerMeasure = meter,
			BeatUnit = int.Parse((string?)this.beatUnit.SelectedItem ?? "4", CultureInfo.InvariantCulture),
			Volume = this.volume.Value, AudioEnabled = this.audio.IsChecked, VisualEnabled = this.visual.IsChecked, AccentFirstBeat = this.accent.IsChecked,
		};
		BookApplicationSession.ValidateMetronome(settings);
		return settings;
	}

	private async void HandleToggle(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			if (this.engine.IsRunning)
			{
				this.engine.Stop();
			}
			else
			{
				await this.engine.StartAsync(this.ReadSettings()).ConfigureAwait(true);
			}
		}).ConfigureAwait(true);

	private async void HandleSave(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			MetronomeSettings settings = this.ReadSettings();
			if (this.songId is Guid id)
			{
				await this.session.SaveSongMetronomeAsync(id, settings).ConfigureAwait(true);
			}
			else
			{
				await this.session.SaveBookMetronomeAsync(settings).ConfigureAwait(true);
			}

			if (this.engine.IsRunning)
			{
				await this.engine.StartAsync(settings).ConfigureAwait(true);
			}

			this.status.Text = this.songId.HasValue ? "Saved metronome settings for this song." : "Saved book metronome defaults.";
		}).ConfigureAwait(true);

	private async void HandleReset(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			this.engine.Stop();
			await this.session.SaveSongMetronomeAsync(this.songId!.Value, null).ConfigureAwait(true);
			this.LoadSettings();
			this.status.Text = "This song now uses the book defaults.";
		}).ConfigureAwait(true);

	private async void HandleClose(object? sender, EventArgs e)
	{
		if (!this.busy)
		{
			this.timer.Stop();
			if (this.songId is null)
			{
				this.engine.Stop();
			}

			await this.Navigation.PopModalAsync().ConfigureAwait(true);
			this.completion.TrySetResult();
		}
	}

	private async Task RunAsync(Func<Task> action)
	{
		if (!this.busy)
		{
			this.busy = true;
			try
			{
				await action().ConfigureAwait(true);
			}
#pragma warning disable CA1031 // Surface audio and validation failures at the UI boundary.
			catch (Exception exception)
			{
				this.status.Text = exception.Message;
			}
#pragma warning restore CA1031
			finally
			{
				this.busy = false;
			}
		}
	}

	#endregion
}
