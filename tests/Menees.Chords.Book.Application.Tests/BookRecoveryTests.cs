#region Using Directives

using System.IO;
using System.Text;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class BookRecoveryTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RecoveryRefreshesSearchAndRenderCachesAndRejectsStaleReview()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), nameof(BookRecoveryTests), Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Original book", device, token);
			await BookImportService.ImportAsync(
				store, location, "first.cho", new MemoryStream(Encoding.UTF8.GetBytes("{title: Before}\n[C]Original")), device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			string archive = Path.Combine(root, "original.mcbbak");
			await session.CreateBackupAsync(archive, token);
			Guid song = session.Database!.Songs.Single().Id;
			await session.GetPresentationAsync(song, cancellationToken: token);
			using BookBackupReview review = await BookBackup.ReviewFileAsync(archive, token);
			long revision = session.Database.Revision.Revision;
			await session.RenameAsync("Later name", device, token);
			string safety = Path.Combine(root, "safety.mcbbak");
			await Should.ThrowAsync<BookStoreConcurrencyException>(() =>
				session.ReplaceFromBackupAsync(review, revision, safety, device, token));
			File.Exists(safety).ShouldBeFalse();
			await session.ReplaceFromBackupAsync(review, session.Database.Revision.Revision, safety, device, token);
			session.Database.Name.ShouldBe("Original book");
			session.Search("Before").Single().Id.ShouldBe(song);
			(await session.GetPresentationAsync(song, cancellationToken: token)).ShouldNotBeNull();
			using BookBackupReview saved = await BookBackup.ReviewFileAsync(safety, token);
			saved.Name.ShouldBe("Later name");
			BookValidationReport report = await BookValidator.ValidateFolderAsync(store, location, token);
			BookIntegrityReport.Format(report).ShouldContain("No integrity problems found.");
			File.Delete(Path.Combine(store.GetDirectory(location), session.Database.SongFiles.Single().RelativePath));
			report = await BookValidator.ValidateFolderAsync(store, location, token);
			BookIntegrityReport.Format(report).ShouldContain("Missing sheet:");
			BookIntegrityReport.Format(report).ShouldContain("Restore as New Book");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void IntegrityReportIdentifiesCurrentInvalidSheetAndExplainsRepairWithoutMutation()
	{
		Guid songId = Guid.NewGuid();
		ChordDatabase database = ChordDatabase.Create("Diagnostic book", Guid.NewGuid());
		database.Songs.Add(new() { Id = songId, Title = "Legacy Song", Artists = ["Synthetic Artist"] });
		database.SongFiles.Add(new() { Id = Guid.NewGuid(), SongId = songId, RelativePath = "Legacy sheet.cho" });
		string before = DatabaseJson.Serialize(database);
		Guid fileId = database.SongFiles.Single().Id;
		BookValidationIssue problem = new(BookValidationIssueKind.InvalidSheetContent, "Found U+0080 at line 39, column 10.", fileId, "Legacy sheet.cho");
		BookValidationReport report = new(database, [problem]);
		string directory = Path.Combine(Path.GetTempPath(), "Diagnostic book");
		string text = BookIntegrityReport.Format(report, directory);
		text.ShouldContain("1 issue(s) found.");
		text.ShouldContain("Invalid sheet content");
		text.ShouldContain("Sheet type: Current sheet");
		text.ShouldContain("Song: Legacy Song — Synthetic Artist");
		text.ShouldContain("File: " + Path.Combine(directory, problem.RelativePath!));
		text.ShouldContain(problem.Message);
		text.ShouldContain("backup outside the book folder");
		text.ShouldContain("Review Folder Changes");
		text.ShouldContain("Validate Again");
		BookIntegrityReport.Format(new(database, [])).ShouldNotContain("Invalid sheet content");
		DatabaseJson.Serialize(database).ShouldBe(before);
	}

	[TestMethod]
	public void IntegrityReportShowsUnreadableCatalogWithoutHistoricalWarnings()
	{
		BookValidationReport report = new(null, [new(BookValidationIssueKind.InvalidDatabase, "Unreadable catalog")]);
		string text = BookIntegrityReport.Format(report);
		text.ShouldContain("Unreadable catalog");
		text.ShouldNotContain("last book open");
		text.ShouldNotContain("Song:");
	}

	#endregion
}