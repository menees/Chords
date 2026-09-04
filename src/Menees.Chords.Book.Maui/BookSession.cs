#region Using Directives

using System.IO;
using System.Text.Json;
using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Owns the currently open native filesystem book.</summary>
public sealed partial class BookSession : IDisposable
{
	#region Private Data

	private const string DefaultBookName = "My ChordBook";
	private const string CurrentBookPreference = "ChordBook.CurrentBookPath";
	private const string DeviceIdPreference = "ChordBook.DeviceId";
	private const string RecentBooksPreference = "ChordBook.RecentBooks";
	private const int MaximumRecentBooks = 10;
	private const string DatabaseFileName = "database.json";
	private const string LegacyCompanyDirectory = "Bill Menees";
	private const string LegacyProductDirectory = "Menees.Chords.Book.Maui";
	private const string ManagedCompanyDirectory = "Menees";
	private const string ManagedProductDirectory = "ChordBook";
	private const string StagePattern = ".*.chordbook-stage-*";
	private const double AbandonedStageMinutes = 1;
	private readonly BookApplicationSession application;
	private FileSystemBookStore? store;
	private BookLocation? location;
	private string? booksRoot;

	#endregion

	#region Public API

	public BookSession(BookApplicationSession application)
	{
		this.application = application;
	}

	public ChordDatabase? Database => this.application.Database;

	public string? DirectoryPath { get; private set; }

	public BookMetadataRefreshResult? LastMetadataRefresh { get; private set; }

	public Guid DeviceId { get; } = GetOrCreateDeviceId();

	public static IReadOnlyList<RecentBook> GetRecentBooks()
		=> [.. LoadRecentBooks().Where(book => File.Exists(Path.Combine(book.Path, DatabaseFileName)))];

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
		await this.application.ReloadAsync(cancellationToken).ConfigureAwait(false);
		return results.Count;
	}

	public IReadOnlyList<SongRow> SearchSongs(string? query, bool includeArchived)
	{
		return
		[
			.. this.application.Search(query, includeArchived)
				.Select(song => new SongRow(
					song.Id,
					song.Title,
					song.DisplayText)),
		];
	}

	public Task OpenRecentAsync(RecentBook book, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(book);
		return this.OpenAsync(book.Path, cancellationToken);
	}

	public async Task RenameAsync(string name, CancellationToken cancellationToken = default)
	{
		await this.application.RenameAsync(name, this.DeviceId, cancellationToken).ConfigureAwait(false);
		this.RememberCurrentBook();
	}

	public async Task<SongPresentation> GetPresentationAsync(Guid songId, CancellationToken cancellationToken = default)
	{
		BookSongPresentation presentation = await this.application.GetPresentationAsync(songId, cancellationToken).ConfigureAwait(false);
		string? pdfPath = null;
		if (presentation.MediaKind == MediaKind.Pdf && presentation.SongFileId is Guid fileId)
		{
			(FileSystemBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			SongFile file = this.Database!.SongFiles.Single(item => item.Id == fileId);
			pdfPath = Path.Combine(activeStore.GetDirectory(activeLocation), file.RelativePath);
		}

		return new(presentation.Title, presentation.Html, pdfPath);
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

	private static List<RecentBook> LoadRecentBooks()
	{
		string? json = Preferences.Default.Get<string?>(RecentBooksPreference, null);
		List<RecentBook>? result = null;
		if (!string.IsNullOrWhiteSpace(json))
		{
			try
			{
				result = JsonSerializer.Deserialize<List<RecentBook>>(json);
			}
			catch (JsonException)
			{
				// Ignore malformed local UI state. Opening a valid book rebuilds it.
			}
		}

		return result ?? [];
	}

	private (FileSystemBookStore Store, BookLocation Location) GetActiveBook()
		=> (this.store ?? throw new InvalidOperationException("No book is open."),
			this.location ?? throw new InvalidOperationException("No book is open."));

	private void RememberCurrentBook()
	{
		if (this.DirectoryPath is string path && this.Database is ChordDatabase database)
		{
			List<RecentBook> books = LoadRecentBooks();
			books.RemoveAll(book => StringComparer.OrdinalIgnoreCase.Equals(book.Path, path));
			books.Insert(0, new(database.Name, path));
			Preferences.Default.Set(RecentBooksPreference, JsonSerializer.Serialize(books.Take(MaximumRecentBooks)));
		}
	}

	private async Task SwitchAsync(
		FileSystemBookStore nextStore,
		BookLocation nextLocation,
		CancellationToken cancellationToken)
	{
		FileSystemBookStore? priorStore = this.store;
		BookLocation? priorLocation = this.location;
		BookMetadataRefreshResult? priorMetadataRefresh = this.LastMetadataRefresh;
		this.store = nextStore;
		this.location = nextLocation;
		try
		{
			BookMetadataRefreshResult metadataRefresh = await BookMetadataRefresh.RefreshAsync(
				nextStore,
				nextLocation,
				this.DeviceId,
				cancellationToken)
				.ConfigureAwait(false);
			await this.application.ActivateAsync(nextStore, nextLocation, cancellationToken).ConfigureAwait(false);
			this.DirectoryPath = nextStore.GetDirectory(nextLocation);
			this.LastMetadataRefresh = metadataRefresh;
			Preferences.Default.Set(CurrentBookPreference, this.DirectoryPath);
			this.RememberCurrentBook();
			priorStore?.Dispose();
		}
		catch
		{
			this.store = priorStore;
			this.location = priorLocation;
			this.LastMetadataRefresh = priorMetadataRefresh;
			nextStore.Dispose();
			throw;
		}
	}

	#endregion
}
