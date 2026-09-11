using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

public sealed partial class BookApplicationSession
{
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

	/// <summary>Resolves explicit song overrides over book defaults without inferring tempo from song text.</summary>
	public MetronomeSettings GetMetronomeSettings(Guid songId)
	{
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		MetronomeSettings defaults = database.BookSettings.DefaultMetronome;
		SongMetronomeOverride? patch = database.Songs.Single(song => song.Id == songId).MetronomeOverride;
		return new()
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
			cancellationToken);
	}
}
