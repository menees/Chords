using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui.Services;

public interface IMetronomeEngine : IDisposable
{
	bool IsRunning { get; }

	int CurrentBeat { get; }

	Task StartAsync(MetronomeSettings settings, CancellationToken cancellationToken = default);

	void Stop();
}
