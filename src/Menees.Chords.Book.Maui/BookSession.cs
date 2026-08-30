#region Using Directives

using System.IO;
using Menees.Chords.Db;
using Menees.Chords.Formatters;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Owns the currently open native filesystem book.</summary>
public sealed partial class BookSession : IDisposable
{
	#region Private Data

	private const string DefaultBookName = "My ChordBook";
	private const string CurrentBookPreference = "ChordBook.CurrentBookPath";
	private const string DeviceIdPreference = "ChordBook.DeviceId";
	private const string DatabaseFileName = "database.json";
	private const string LegacyCompanyDirectory = "Bill Menees";
	private const string LegacyProductDirectory = "Menees.Chords.Book.Maui";
	private const string ManagedCompanyDirectory = "Menees";
	private const string ManagedProductDirectory = "ChordBook";
	private const string StagePattern = ".*.chordbook-stage-*";
	private const double AbandonedStageMinutes = 1;
	private FileSystemBookStore? store;
	private BookLocation? location;
	private string? booksRoot;

	#endregion

	#region Public API

	public ChordDatabase? Database { get; private set; }

	public string? DirectoryPath { get; private set; }

	public Guid DeviceId { get; } = GetOrCreateDeviceId();

	public void Dispose() => this.store?.Dispose();

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		string nextBooksRoot = GetManagedBooksRoot();
		await Task.Run(() => PrepareManagedBooksRoot(nextBooksRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
		this.booksRoot = nextBooksRoot;
		string? existing = GetInitialBookDirectory(nextBooksRoot);
		FileSystemBookStore nextStore = new(nextBooksRoot);
		BookLocation nextLocation = existing is null
			? await nextStore.CreateBookAsync(DefaultBookName, this.DeviceId, cancellationToken).ConfigureAwait(false)
			: await nextStore.OpenBookAsync(existing, cancellationToken).ConfigureAwait(false);
		await this.SwitchAsync(nextStore, nextLocation, cancellationToken).ConfigureAwait(false);
	}

	public async Task CreateAsync(string name, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		string activeBooksRoot = this.booksRoot ?? throw new InvalidOperationException("ChordBook has not been initialized.");
		FileSystemBookStore nextStore = new(activeBooksRoot);
		BookLocation nextLocation;
		try
		{
			nextLocation = await nextStore.CreateBookAsync(name.Trim(), this.DeviceId, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			nextStore.Dispose();
			throw;
		}

		await this.SwitchAsync(nextStore, nextLocation, cancellationToken).ConfigureAwait(false);
	}

	public async Task OpenAsync(string directory, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		string fullPath = Path.GetFullPath(directory);
		FileSystemBookStore nextStore = new(Path.GetDirectoryName(fullPath)!);
		BookLocation nextLocation;
		try
		{
			nextLocation = await nextStore.OpenBookAsync(fullPath, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			nextStore.Dispose();
			throw;
		}

		await this.SwitchAsync(nextStore, nextLocation, cancellationToken).ConfigureAwait(false);
	}

	public async Task<int> ImportAsync(IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourcePaths);
		(FileSystemBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		IReadOnlyList<BookImportResult> results = await BookImportService.ImportFilesAsync(
			activeStore,
			activeLocation,
			sourcePaths,
			this.DeviceId,
			cancellationToken).ConfigureAwait(false);
		await this.ReloadAsync(cancellationToken).ConfigureAwait(false);
		return results.Count;
	}

	public IReadOnlyList<SongRow> GetSongs()
	{
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		return
		[
			.. database.Songs
				.Where(song => !song.IsArchived)
				.OrderBy(song => song.Title, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(song => song.Id)
				.Select(song => new SongRow(song.Id, song.Title, string.Join(", ", song.Artists))),
		];
	}

	public async Task<SongPresentation> GetPresentationAsync(Guid songId, CancellationToken cancellationToken = default)
	{
		(FileSystemBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		Song song = database.Songs.Single(item => item.Id == songId);
		SongFile? file = database.SongFiles
			.Where(item => item.SongId == songId && !item.IsArchived)
			.OrderByDescending(item => item.DisplayPriority)
			.ThenBy(item => item.MediaKind)
			.ThenBy(item => item.Id)
			.FirstOrDefault();
		SongPresentation result;
		if (file is null)
		{
			result = new SongPresentation(song.Title, "<html><body><p>This song has no active file.</p></body></html>", null);
		}
		else if (file.MediaKind == MediaKind.Pdf)
		{
			string pdfPath = Path.Combine(activeStore.GetDirectory(activeLocation), file.RelativePath);
			result = new SongPresentation(song.Title, null, pdfPath);
		}
		else
		{
			using Stream stream = await activeStore.OpenManagedAssetAsync(activeLocation, file.Id, cancellationToken).ConfigureAwait(false);
			Document document = Document.Load(stream);
			result = new SongPresentation(song.Title, new HtmlFormatter(document).ToString(), null);
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static void CleanupAbandonedStages(string rootDirectory)
	{
		if (Directory.Exists(rootDirectory))
		{
			DateTime oldestActiveWrite = DateTime.UtcNow - TimeSpan.FromMinutes(AbandonedStageMinutes);
			foreach (string directory in Directory.EnumerateDirectories(rootDirectory, StagePattern, SearchOption.TopDirectoryOnly))
			{
				if (Directory.GetLastWriteTimeUtc(directory) < oldestActiveWrite)
				{
					Directory.Delete(directory, recursive: true);
				}
			}
		}
	}

	private static void CopyDirectory(string source, string target, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(target);
		foreach (string sourceDirectory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
		{
			cancellationToken.ThrowIfCancellationRequested();
			Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, sourceDirectory)));
		}

		foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string targetFile = Path.Combine(target, Path.GetRelativePath(source, sourceFile));
			File.Copy(sourceFile, targetFile, overwrite: true);
			File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(sourceFile));
		}
	}

	private static string? GetInitialBookDirectory(string booksRoot)
	{
		string? preferred = Preferences.Default.Get<string?>(CurrentBookPreference, null);
		string? result = preferred is not null && File.Exists(Path.Combine(preferred, DatabaseFileName))
			? preferred
			: Directory.EnumerateDirectories(booksRoot, "*", SearchOption.TopDirectoryOnly)
				.Where(directory => !Path.GetFileName(directory).StartsWith('.'))
				.Where(directory => File.Exists(Path.Combine(directory, DatabaseFileName)))
				.OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault();
		return result;
	}

	private static string GetManagedBooksRoot()
		=> Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			ManagedCompanyDirectory,
			ManagedProductDirectory,
			"Books");

	private static void PrepareManagedBooksRoot(string booksRoot, CancellationToken cancellationToken)
	{
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string legacyBooksRoot = Path.Combine(
			localAppData,
			LegacyCompanyDirectory,
			LegacyProductDirectory,
			"Data",
			"Books");
		Directory.CreateDirectory(booksRoot);
		CleanupAbandonedStages(booksRoot);
		if (!StringComparer.OrdinalIgnoreCase.Equals(legacyBooksRoot, booksRoot) && Directory.Exists(legacyBooksRoot))
		{
			CleanupAbandonedStages(legacyBooksRoot);
			foreach (string source in Directory.EnumerateDirectories(legacyBooksRoot, "*", SearchOption.TopDirectoryOnly))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (File.Exists(Path.Combine(source, DatabaseFileName)))
				{
					string target = Path.Combine(booksRoot, Path.GetFileName(source));
					if (!File.Exists(Path.Combine(target, DatabaseFileName)))
					{
						CopyDirectory(source, target, cancellationToken);
					}
				}
			}
		}
	}

	private static Guid GetOrCreateDeviceId()
	{
		string? saved = Preferences.Default.Get<string?>(DeviceIdPreference, null);
		Guid result;
		if (Guid.TryParse(saved, out Guid parsed))
		{
			result = parsed;
		}
		else
		{
			result = Guid.NewGuid();
			Preferences.Default.Set(DeviceIdPreference, result.ToString("D"));
		}

		return result;
	}

	private (FileSystemBookStore Store, BookLocation Location) GetActiveBook()
		=> (this.store ?? throw new InvalidOperationException("No book is open."),
			this.location ?? throw new InvalidOperationException("No book is open."));

	private async Task ReloadAsync(CancellationToken cancellationToken)
	{
		(FileSystemBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		this.Database = DatabaseJson.Deserialize(
			await activeStore.ReadDatabaseJsonAsync(activeLocation, cancellationToken).ConfigureAwait(false));
		this.DirectoryPath = activeStore.GetDirectory(activeLocation);
	}

	private async Task SwitchAsync(
		FileSystemBookStore nextStore,
		BookLocation nextLocation,
		CancellationToken cancellationToken)
	{
		FileSystemBookStore? priorStore = this.store;
		BookLocation? priorLocation = this.location;
		this.store = nextStore;
		this.location = nextLocation;
		try
		{
			await this.ReloadAsync(cancellationToken).ConfigureAwait(false);
			Preferences.Default.Set(CurrentBookPreference, this.DirectoryPath);
			priorStore?.Dispose();
		}
		catch
		{
			this.store = priorStore;
			this.location = priorLocation;
			nextStore.Dispose();
			throw;
		}
	}

	#endregion
}
