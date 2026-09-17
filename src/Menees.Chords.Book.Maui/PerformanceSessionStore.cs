#region Using Directives

using System.Text.Json;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Persists navigation context once per selection and a small cursor per song, outside the book.</summary>
internal sealed class PerformanceSessionStore
{
	#region Private Data

	private readonly SemaphoreSlim gate = new(1, 1);
	private IReadOnlyList<SongRow>? source;
	private PerformanceSessionContext? context;

	#endregion

	#region Public Methods

	public async Task RecordAsync(
		Guid bookId, Guid? setlistId, IReadOnlyList<SongRow> songs, int index, Guid? entryId, string? name, CancellationToken cancellationToken = default)
	{
		if (index >= 0 && index < songs.Count && songs.Count <= PerformanceSessionRecovery.MaximumSongs)
		{
			await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (!ReferenceEquals(this.source, songs) || this.context?.BookId != bookId || this.context.SetlistId != setlistId)
				{
					PerformanceSessionContext next = new(Guid.NewGuid(), bookId, setlistId, [.. songs.Select(song => song.Id)], name);
					string path = GetPath(bookId);
					await Task.Run(
						() =>
						{
							Directory.CreateDirectory(Path.GetDirectoryName(path)!);
							string stage = path + ".tmp";
							File.WriteAllText(stage, JsonSerializer.Serialize(next));
							File.Move(stage, path, overwrite: true);
						},
						cancellationToken).ConfigureAwait(false);
					this.source = songs;
					this.context = next;
				}

				PerformanceSessionCursor cursor = new(this.context!.Id, songs[index].Id, entryId);
				Preferences.Default.Set(GetCursorKey(bookId), JsonSerializer.Serialize(cursor));
			}
			finally
			{
				this.gate.Release();
			}
		}
		else
		{
			Preferences.Default.Remove(GetCursorKey(bookId));
		}
	}

	public async Task<PerformanceResume?> LoadAsync(ChordDatabase database, CancellationToken cancellationToken = default)
	{
		PerformanceResume? result = null;
		await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			string path = GetPath(database.Id);
			string? cursor = Preferences.Default.Get<string?>(GetCursorKey(database.Id), null);
			result = await Task.Run(
				() =>
				{
					PerformanceResume? loaded = null;
					if (File.Exists(path) && cursor is not null && cursor.Length <= PerformanceSessionRecovery.MaximumJsonCharacters)
					{
						using FileStream stream = File.OpenRead(path);
						if (stream.Length <= PerformanceSessionRecovery.MaximumJsonCharacters)
						{
							loaded = PerformanceSessionRecovery.Resolve(
								database,
								JsonSerializer.Deserialize<PerformanceSessionContext>(stream),
								JsonSerializer.Deserialize<PerformanceSessionCursor>(cursor));
						}
					}

					return loaded;
				},
				cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			// An unavailable local hint must not block opening a book.
		}
		finally
		{
			this.gate.Release();
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static string GetCursorKey(Guid bookId) => "ChordBook.PerformanceCursor." + bookId.ToString("D");

	private static string GetPath(Guid bookId)
		=> Path.Combine(FileSystem.AppDataDirectory, "Performance", bookId.ToString("D") + ".json");

	#endregion
}
