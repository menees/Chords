using Menees.Chords.Book.Application;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui.Services;

public interface IMetronomeEngine : IDisposable
{
	MetronomeSettings CurrentSettings { get; }

	bool IsRunning { get; }

	int CurrentBeat { get; }

	int BeatsPerMeasure { get; }

	bool VisualEnabled { get; }

	bool AccentFirstBeat { get; }

	MetronomeVisualState? VisualPulse { get; }

	Task StartAsync(MetronomeSettings settings, CancellationToken cancellationToken = default);

	void Stop();
}
