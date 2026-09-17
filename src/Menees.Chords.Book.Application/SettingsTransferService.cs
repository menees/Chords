#region Using Directives

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Exports and applies portable settings without transferring book or device identity.</summary>
public sealed class SettingsTransferService
{
	#region Private Data

	// Allows escaped Unicode at every supported field limit while bounding file reads.
	private const int MaximumJsonBytes = 128 * 1024;
	private const int MaximumTemplateLength = 4096;
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
	};

	private readonly BookApplicationSession session;

	#endregion

	#region Constructors

	public SettingsTransferService(BookApplicationSession session) => this.session = session;

	#endregion

	#region Public Methods

	public static PortableBookSettings Read(string json)
	{
		ArgumentNullException.ThrowIfNull(json);
		ValidateJsonSize(json);

		PortableBookSettings settings = JsonSerializer.Deserialize<PortableBookSettings>(json, Options)
			?? throw new JsonException("The settings file is empty.");
		Validate(settings);
		return settings;
	}

	public static async Task<PortableBookSettings> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length > MaximumJsonBytes)
		{
			throw new ArgumentException("The settings file is too large.", nameof(path));
		}

		using StreamReader reader = new(input);
		return Read(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
	}

	public string Export()
	{
		BookSettings settings = this.session.Database?.BookSettings ?? throw new InvalidOperationException("No book is open.");
		PortableBookSettings portable = new()
		{
			FormatVersion = 1,
			Display = settings.DefaultDisplayProfile,
			Metronome = settings.DefaultMetronome,
			StopMetronomeOnSetlistTransition = settings.StopMetronomeOnSetlistTransition,
			InputBindings = settings.InputBindings,
			TitleTemplate = settings.TitleTemplate,
			SubtitleTemplate = settings.SubtitleTemplate,
		};
		Validate(portable);
		string json = JsonSerializer.Serialize(portable, Options);
		ValidateJsonSize(json);
		return json;
	}

	public async Task ExportFileAsync(string path, CancellationToken cancellationToken = default)
	{
		if (!string.Equals(Path.GetExtension(path), ".mcbsettings", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Choose a .mcbsettings file for the settings backup.", nameof(path));
		}

		string json = this.Export();
		string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			await File.WriteAllTextAsync(stage, json, cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			File.Move(stage, path, overwrite: true);
		}
		finally
		{
			File.Delete(stage);
		}
	}

	public Task ApplyAsync(
		PortableBookSettings settings, Guid bookId, long settingsRevision, Guid deviceId, CancellationToken cancellationToken = default)
	{
		Validate(settings);
		PortableBookSettings snapshot = Read(JsonSerializer.Serialize(settings, Options));
		return this.session.MutateMetadataAsync(
			(database, now) =>
			{
				if (database.Id != bookId || database.BookSettings.Revision.Revision != settingsRevision)
				{
					throw new InvalidOperationException("Book settings changed. Review the settings file again before restoring.");
				}

				database.BookSettings = new()
				{
					DefaultDisplayProfile = snapshot.Display,
					DefaultMetronome = snapshot.Metronome,
					StopMetronomeOnSetlistTransition = snapshot.StopMetronomeOnSetlistTransition,
					InputBindings = snapshot.InputBindings,
					TitleTemplate = snapshot.TitleTemplate,
					SubtitleTemplate = snapshot.SubtitleTemplate,
					Revision = BookApplicationSession.NextRevision(database.BookSettings.Revision, deviceId, now),
				};
			},
			deviceId,
			cancellationToken);
	}

	#endregion

	#region Private Methods

	private static void ValidateJsonSize(string json)
	{
		if (json.Length > MaximumJsonBytes || Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
		{
			throw new ArgumentException("The settings file is too large.", nameof(json));
		}
	}

	private static void Validate(PortableBookSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		if (settings.FormatVersion != 1 || settings.Display is null || settings.Metronome is null || settings.InputBindings is null
			|| settings.TitleTemplate is null || settings.SubtitleTemplate is null || settings.TitleTemplate.Length > MaximumTemplateLength
			|| settings.SubtitleTemplate.Length > MaximumTemplateLength)
		{
			throw new ArgumentException("The settings file is incomplete or uses an unsupported format version.", nameof(settings));
		}

		SongDisplaySettings.Validate(settings.Display);
		BookApplicationSession.ValidateMetronome(settings.Metronome);
		PerformanceInputService.Validate(settings.InputBindings);
	}

	#endregion
}
