#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Services;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

public sealed partial class MetronomePanel : ContentView
{
	#region Private Data

	private const int MaximumBeats = 16;
	private const int DefaultBeats = 4;
	private static readonly string[] Sounds = ["KickHiHat", "Kick", "HiHat", "Woodblock", "Cowbell", "Click"];
	private readonly Guid? songId;
	private readonly BookSession session;
	private readonly IMetronomeEngine engine;
	private readonly IDispatcherTimer timer;

	private bool busy;
	private bool detached;
	private bool loading;
	private bool liveUpdating;
	private bool liveUpdatePending;

	#endregion

	#region Public API

	public MetronomePanel(Guid? songId, BookSession session, IMetronomeEngine engine)
	{
		this.songId = songId;
		this.session = session;
		this.engine = engine;
		this.InitializeComponent();
		this.saveSettings.SetIcon(FluentIconButton.Save, songId.HasValue ? "Save for This Song" : "Save Book Defaults");
		this.closePanel.SetIcon(FluentIconButton.Close, "Close metronome controls");
		this.resetSettings.IsVisible = songId.HasValue;
		this.beatUnit.ItemsSource = new[] { "2", "4", "8", "16" };
		this.subdivision.ItemsSource = new[] { "1", "2", "3 (triplets)", "4" };
		this.sound.ItemsSource = new[] { "Kick and hi-hat", "Kick", "Hi-hat", "Woodblock", "Cowbell", "Legacy click" };
		this.transitionSettings.IsVisible = !songId.HasValue;
		this.LoadSettings(usePlayback: true);
		this.bpm.TextChanged += this.HandleLiveSettingsChanged;
		this.beats.TextChanged += this.HandleLiveSettingsChanged;
		this.beatUnit.SelectedIndexChanged += this.HandleLiveSettingsChanged;
		this.subdivision.SelectedIndexChanged += this.HandleLiveSettingsChanged;
		this.sound.SelectedIndexChanged += this.HandleLiveSettingsChanged;
		this.volume.ValueChanged += this.HandleLiveSettingsChanged;
		this.audio.CheckedChanged += this.HandleLiveSettingsChanged;
		this.visual.CheckedChanged += this.HandleLiveSettingsChanged;
		this.accent.CheckedChanged += this.HandleLiveSettingsChanged;
		this.timer = this.Dispatcher.CreateTimer();
		this.timer.Interval = TimeSpan.FromMilliseconds(50);
		this.timer.Tick += (_, _) =>
		{
			this.toggle.SetIcon(this.engine.IsRunning ? FluentIconButton.Stop : FluentIconButton.Play, this.engine.IsRunning ? "Stop" : "Start");
			this.pulse.IsVisible = this.visual.IsChecked;
			this.pulse.Update(
				this.engine.IsRunning ? this.engine.BeatsPerMeasure : this.BeatCount,
				this.engine.IsRunning && this.engine.VisualEnabled ? this.engine.CurrentBeat : 0,
				this.accent.IsChecked && this.engine.CurrentBeat == 1);
		};
		this.Loaded += (_, _) => this.timer.Start();
		this.Unloaded += (_, _) => this.timer.Stop();
	}

	public event EventHandler? Closed;

	public event EventHandler? SettingsSaved;

	public bool IsBusy => this.busy || this.liveUpdating;

	public int BeatCount => int.TryParse(this.beats.Text, out int value) ? Math.Clamp(value, 1, MaximumBeats) : DefaultBeats;

	public void Detach()
	{
		this.detached = true;
		this.timer.Stop();
	}

	#endregion

	#region Private Methods

	private async void HandleLiveSettingsChanged(object? sender, EventArgs e)
	{
		if (!this.loading && !this.detached)
		{
			if (sender == this.sound && this.sound.SelectedIndex == 0 && !this.accent.IsChecked)
			{
				this.accent.IsChecked = true;
			}
			else if (sender == this.accent && !this.accent.IsChecked && this.sound.SelectedIndex == 0)
			{
				this.sound.SelectedIndex = Array.IndexOf(Sounds, "HiHat");
			}

			this.pulse.IsVisible = this.visual.IsChecked;
			await this.ApplyPlaybackAsync().ConfigureAwait(true);
		}
	}

	private async Task ApplyPlaybackAsync(bool start = false)
	{
		if (!this.detached)
		{
			this.liveUpdatePending = true;
			if (!this.liveUpdating)
			{
				this.liveUpdating = true;
				try
				{
					while (this.liveUpdatePending && !this.detached)
					{
						this.liveUpdatePending = false;
						try
						{
							MetronomeSettings settings = this.ReadSettings();
							bool apply = start || this.engine.IsRunning;
							start = false;
							if (apply)
							{
								await this.engine.StartAsync(settings).ConfigureAwait(true);
							}

							this.status.Text = string.Empty;
						}
#pragma warning disable CA1031 // Keep invalid intermediate numeric input and device errors in the panel.
						catch (Exception exception)
						{
							this.status.Text = exception.Message;
						}
#pragma warning restore CA1031
					}
				}
				finally
				{
					this.liveUpdating = false;
				}
			}
		}
	}

	private void LoadSettings(bool usePlayback = false)
	{
		this.loading = true;
		MetronomeSettings settings = usePlayback && this.engine.IsRunning ? this.engine.CurrentSettings : this.session.GetMetronomeSettings(this.songId);
		this.bpm.Text = settings.BeatsPerMinute.ToString(CultureInfo.CurrentCulture);
		this.beats.Text = settings.BeatsPerMeasure.ToString(CultureInfo.CurrentCulture);
		this.beatUnit.SelectedItem = settings.BeatUnit.ToString(CultureInfo.InvariantCulture);
		this.subdivision.SelectedIndex = settings.Subdivision - 1;
		this.sound.SelectedIndex = Array.IndexOf(Sounds, settings.Sound == "KickHiHat" && !settings.AccentFirstBeat ? "HiHat" : settings.Sound);
		this.stopOnTransition.IsChecked = this.session.Database!.BookSettings.StopMetronomeOnSetlistTransition;
		this.volume.Value = settings.Volume;
		this.audio.IsChecked = settings.AudioEnabled;
		this.visual.IsChecked = settings.VisualEnabled;
		this.accent.IsChecked = settings.AccentFirstBeat;
		this.loading = false;
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
			Subdivision = this.subdivision.SelectedIndex + 1,
			Sound = this.sound.SelectedIndex >= 0 ? Sounds[this.sound.SelectedIndex] : string.Empty,
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
				await this.ApplyPlaybackAsync(start: true).ConfigureAwait(true);
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
				await this.session.SaveBookMetronomeAsync(settings, this.stopOnTransition.IsChecked).ConfigureAwait(true);
			}

			this.SettingsSaved?.Invoke(this, EventArgs.Empty);
			this.status.Text = this.songId.HasValue ? "Saved metronome settings for this song." : "Saved book metronome defaults.";
		}).ConfigureAwait(true);

	private async void HandleReset(object? sender, EventArgs e)
		=> await this.RunAsync(async () =>
		{
			await this.session.SaveSongMetronomeAsync(this.songId!.Value, null).ConfigureAwait(true);
			this.LoadSettings();
			if (!this.detached && this.engine.IsRunning)
			{
				await this.ApplyPlaybackAsync().ConfigureAwait(true);
			}

			this.status.Text = "This song now uses the book defaults.";
			this.SettingsSaved?.Invoke(this, EventArgs.Empty);
		}).ConfigureAwait(true);

	private void HandleClose(object? sender, EventArgs e)
	{
		if (!this.IsBusy)
		{
			this.Detach();
			this.Closed?.Invoke(this, EventArgs.Empty);
		}
	}

	private async Task RunAsync(Func<Task> action)
	{
		if (!this.IsBusy)
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
