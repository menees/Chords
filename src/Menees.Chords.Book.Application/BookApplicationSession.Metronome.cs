#region Using Directives

using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

public sealed partial class BookApplicationSession
{
	#region Public Methods

	public static bool MetronomeSettingsEqual(MetronomeSettings left, MetronomeSettings right)
		=> left.BeatsPerMinute == right.BeatsPerMinute && left.BeatsPerMeasure == right.BeatsPerMeasure && left.BeatUnit == right.BeatUnit
			&& left.Subdivision == right.Subdivision && left.Sound == right.Sound && left.Volume == right.Volume
			&& left.AccentFirstBeat == right.AccentFirstBeat && left.AudioEnabled == right.AudioEnabled && left.VisualEnabled == right.VisualEnabled;

	/// <summary>Validates the supported initial click-engine settings.</summary>
	public static void ValidateMetronome(MetronomeSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		const int MinimumTempo = 20;
		const int MaximumTempo = 300;
		const int MaximumBeats = 16;
		const int QuarterNote = 4;
		const int EighthNote = 8;
		const int SixteenthNote = 16;
		int[] beatUnits = [2, QuarterNote, EighthNote, SixteenthNote];
		if (settings.BeatsPerMinute is < MinimumTempo or > MaximumTempo || settings.BeatsPerMeasure is < 1 or > MaximumBeats
			|| !beatUnits.Contains(settings.BeatUnit) || !double.IsFinite(settings.Volume) || settings.Volume is < 0 or > 1
			|| (!settings.AudioEnabled && !settings.VisualEnabled) || settings.Subdivision != 1 || settings.Sound != "Click")
		{
			throw new ArgumentException(
				"Use 20–300 BPM, 1–16 beats, a beat unit of 2, 4, 8, or 16, and audio or visual feedback. This engine supports one Click per beat.",
				nameof(settings));
		}
	}

	public Task SaveBookMetronomeAsync(MetronomeSettings settings, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ValidateMetronome(settings);
		return this.MutateMetadataAsync(
			(database, now) =>
			{
				database.BookSettings.DefaultMetronome = ResolveMetronome(settings, null);
				database.BookSettings.Revision = NextRevision(database.BookSettings.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);
	}

	/// <summary>Resolves explicit song overrides over book defaults without inferring tempo from song text.</summary>
	public MetronomeSettings GetMetronomeSettings(Guid? songId = null)
	{
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		MetronomeSettings defaults = database.BookSettings.DefaultMetronome;
		SongMetronomeOverride? patch = songId is Guid id ? database.Songs.Single(song => song.Id == id).MetronomeOverride : null;
		return ResolveMetronome(defaults, patch);
	}

	/// <summary>Saves metronome controls as explicit song settings, or resets the song to book defaults.</summary>
	public Task SaveSongMetronomeAsync(Guid songId, MetronomeSettings? settings, Guid deviceId, CancellationToken cancellationToken = default)
	{
		if (settings is not null)
		{
			ValidateMetronome(settings);
		}

		return this.MutateMetadataAsync(
			(database, now) =>
			{
				Song song = database.Songs.Single(item => item.Id == songId);
				song.MetronomeOverride = settings is null ? null : new()
				{
					BeatsPerMinute = settings.BeatsPerMinute, BeatsPerMeasure = settings.BeatsPerMeasure, BeatUnit = settings.BeatUnit,
					Subdivision = settings.Subdivision, Sound = settings.Sound, Volume = settings.Volume,
					AccentFirstBeat = settings.AccentFirstBeat, AudioEnabled = settings.AudioEnabled, VisualEnabled = settings.VisualEnabled,
				};
				song.Revision = NextRevision(song.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken,
			refreshPredicatesFor: songId);
	}

	#endregion

	#region Private Methods

	private static MetronomeSettings ResolveMetronome(MetronomeSettings defaults, SongMetronomeOverride? patch) => new()
	{
		BeatsPerMinute = patch?.BeatsPerMinute ?? defaults.BeatsPerMinute,
		BeatsPerMeasure = patch?.BeatsPerMeasure ?? defaults.BeatsPerMeasure,
		BeatUnit = patch?.BeatUnit ?? defaults.BeatUnit,
		Subdivision = patch?.Subdivision ?? defaults.Subdivision,
		Sound = patch?.Sound ?? defaults.Sound,
		Volume = patch?.Volume ?? defaults.Volume,
		AccentFirstBeat = patch?.AccentFirstBeat ?? defaults.AccentFirstBeat,
		AudioEnabled = patch?.AudioEnabled ?? defaults.AudioEnabled,
		VisualEnabled = patch?.VisualEnabled ?? defaults.VisualEnabled,
	};

	#endregion
}
