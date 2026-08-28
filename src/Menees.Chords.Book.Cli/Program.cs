#region Using Directives

using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Cli;

internal static class Program
{
	#region Private Data

	private const int UsageError = 2;
	private const int ValidationError = 3;
	private const int CommandAndTwoArguments = 3;
	private const int CommandAndThreeArguments = 4;
	private static readonly Guid HarnessDeviceId = Guid.Parse("4d2d713e-a096-4e0b-bf23-3e32af08c87a");

	#endregion

	#region Private Methods

#pragma warning disable CC0068 // Main is the executable entry point.
#pragma warning disable CC0061 // Main must retain its runtime-recognized name.
	private static async Task<int> Main(string[] args)
#pragma warning restore CC0061
#pragma warning restore CC0068
	{
		int result;
		try
		{
			result = args.Length == 0 ? ShowUsage() : await RunAsync(args).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // A command-line boundary must translate all failures into a nonzero exit code.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			Console.Error.WriteLine(exception.Message);
			result = 1;
		}

		return result;
	}

	private static async Task<int> RunAsync(string[] args)
	{
		string command = args[0].ToLowerInvariant();
		int result = command switch
		{
			"load" when args.Length == 2 => await LoadAsync(args[1]).ConfigureAwait(false),
			"create" when args.Length == CommandAndTwoArguments => await CreateAsync(args[1], args[2]).ConfigureAwait(false),
			"validate" when args.Length == 2 => await ValidateAsync(args[1]).ConfigureAwait(false),
			"reconcile" when args.Length is 2 or CommandAndTwoArguments => await ReconcileAsync(
				args[1],
				args.Length == CommandAndTwoArguments && args[2] == "--apply").ConfigureAwait(false),
			"search" when args.Length >= CommandAndTwoArguments => await SearchAsync(args[1], string.Join(' ', args.Skip(2))).ConfigureAwait(false),
			"backup" when args.Length == CommandAndTwoArguments => await BackupAsync(args[1], args[2]).ConfigureAwait(false),
			"restore" when args.Length is CommandAndTwoArguments or CommandAndThreeArguments => await RestoreAsync(
				args[1],
				args[2],
				args.ElementAtOrDefault(CommandAndTwoArguments)).ConfigureAwait(false),
			"import" when args.Length == CommandAndTwoArguments => await ImportAsync(args[1], args[2]).ConfigureAwait(false),
			_ => ShowUsage(),
		};
		return result;
	}

	private static async Task<int> BackupAsync(string bookDirectory, string outputPath)
	{
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory).ConfigureAwait(false);
		await BookBackup.CreateFileAsync(store, location, outputPath).ConfigureAwait(false);
		Console.WriteLine(Path.GetFullPath(outputPath));
		return 0;
	}

	private static FileSystemBookStore CreateStore(string bookDirectory)
	{
		string fullPath = Path.GetFullPath(bookDirectory);
		string root = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("Book folder must have a parent directory.", nameof(bookDirectory));
		return new(root);
	}

	private static async Task<int> CreateAsync(string booksRoot, string name)
	{
		using FileSystemBookStore store = new(booksRoot);
		BookLocation location = await store.CreateBookAsync(name, HarnessDeviceId).ConfigureAwait(false);
		Console.WriteLine(store.GetDirectory(location));
		return 0;
	}

	private static async Task<int> ImportAsync(string bookDirectory, string sourcePath)
	{
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory).ConfigureAwait(false);
		BookImportResult imported = await BookImportService.ImportFileAsync(store, location, sourcePath, HarnessDeviceId).ConfigureAwait(false);
		Console.WriteLine($"{imported.Analysis.SourceFormat}: {imported.Analysis.Title} -> {imported.RelativePath}");
		return 0;
	}

	private static async Task<int> LoadAsync(string bookDirectory)
	{
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location).ConfigureAwait(false));
		Console.WriteLine($"{database.Name}: {database.Songs.Count} songs, {database.SongFiles.Count} files, {database.Setlists.Count} setlists");
		return 0;
	}

	private static async Task<int> ReconcileAsync(string bookDirectory, bool apply)
	{
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory).ConfigureAwait(false);
		IReadOnlyList<ExternalBookProblem> problems;
		if (apply)
		{
			BookReconcileResult result = await store.ReconcileAsync(location, HarnessDeviceId).ConfigureAwait(false);
			Console.WriteLine($"Adopted {result.RenamedFileCount} renames and {result.ChangedFileCount} content edits.");
			problems = result.Problems;
		}
		else
		{
			problems = await store.InspectAsync(location).ConfigureAwait(false);
		}

		foreach (ExternalBookProblem problem in problems)
		{
			Console.WriteLine($"{problem.RelativePath}: {problem.Message}");
		}

		return problems.Count == 0 ? 0 : ValidationError;
	}

	private static async Task<int> RestoreAsync(string backupPath, string booksRoot, string? name)
	{
		using FileSystemBookStore store = new(booksRoot);
		await using FileStream input = File.OpenRead(backupPath);
		BookLocation location = await BookBackup.RestoreAsNewAsync(store, input, HarnessDeviceId, name).ConfigureAwait(false);
		Console.WriteLine(store.GetDirectory(location));
		return 0;
	}

	private static async Task<int> SearchAsync(string bookDirectory, string query)
	{
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location).ConfigureAwait(false));
		foreach (BookSearchHit match in new BookSearchIndex(database).Search(query))
		{
			Console.WriteLine($"{match.Title}\t{string.Join(", ", match.Artists)}\t{match.SongId}");
		}

		return 0;
	}

	private static int ShowUsage()
	{
		Console.Error.WriteLine(
			"""
			Usage: chordbook <command>
			  create <books-root> <name>
			  load <book-folder>
			  validate <book-folder>
			  reconcile <book-folder> [--apply]
			  search <book-folder> <query>
			  import <book-folder> <source-file>
			  backup <book-folder> <output.mcbbak>
			  restore <input.mcbbak> <books-root> [new-name]
			""");
		return UsageError;
	}

	private static async Task<int> ValidateAsync(string bookDirectory)
	{
		using FileSystemBookStore store = CreateStore(bookDirectory);
		BookLocation location = await store.OpenBookAsync(bookDirectory).ConfigureAwait(false);
		BookValidationReport report = await BookValidator.ValidateAsync(store, location).ConfigureAwait(false);
		foreach (BookValidationIssue issue in report.Issues)
		{
			Console.WriteLine($"{issue.Kind}: {issue.RelativePath} {issue.Message}");
		}

		Console.WriteLine(report.IsValid ? "Valid." : $"Invalid: {report.Issues.Count} issue(s).");
		return report.IsValid ? 0 : ValidationError;
	}

	#endregion
}
