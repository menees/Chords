#region Using Directives

using System.IO;
using System.Text;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Formats a read-only integrity check for display or copying.</summary>
public static class BookIntegrityReport
{
	#region Public Methods

	/// <summary>Creates a human-readable current validation report with suggested next steps and no source modifications.</summary>
	public static string Format(BookValidationReport report, string? bookDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(report);
		StringBuilder text = new();
		text.AppendLine("ChordBook Integrity Report");
		if (report.Database is ChordDatabase database)
		{
			text.AppendLine(database.Name);
			text.AppendLine($"Songs: {database.Songs.Count}; sheets: {database.SongFiles.Count}; setlists: {database.Setlists.Count}");
		}

		if (!string.IsNullOrEmpty(bookDirectory))
		{
			text.AppendLine($"Book folder: {bookDirectory}");
		}

		text.AppendLine();
		text.AppendLine(report.IsValid ? "No integrity problems found." : $"{report.Issues.Count} issue(s) found.");
		text.AppendLine("This check did not change any book files.");
		Dictionary<Guid, SongFile> files = report.Database?.SongFiles.ToDictionary(file => file.Id) ?? [];
		Dictionary<Guid, Song> songs = report.Database?.Songs.ToDictionary(song => song.Id) ?? [];
		foreach (BookValidationIssue issue in report.Issues)
		{
			text.AppendLine();
			text.AppendLine($"{Describe(issue.Kind)}: {issue.RelativePath ?? "Book database"}");
			if (issue.SongFileId is Guid id && files.TryGetValue(id, out SongFile? file))
			{
				if (songs.TryGetValue(file.SongId, out Song? song))
				{
					text.AppendLine($"Song: {song.Title}" + (song.Artists.Count > 0 ? $" — {string.Join(", ", song.Artists)}" : string.Empty));
				}

				string kind = file.RecoveryVersion is not null ? "Recovery sheet"
					: file.IsArchived ? "Archived sheet" : "Current sheet";
				text.AppendLine($"Sheet type: {kind}");
			}

			if (!string.IsNullOrEmpty(bookDirectory) && issue.RelativePath is not null)
			{
				text.AppendLine($"File: {Path.Combine(bookDirectory, issue.RelativePath)}");
			}

			text.AppendLine(issue.Message);
		}

		if (!report.IsValid)
		{
			text.AppendLine();
			text.AppendLine("Next steps");
			text.AppendLine("Review Folder Changes can reconcile intended edits and renames.");
			text.AppendLine("Keep copies of duplicate or unreferenced sheets before deciding which to import or remove.");
			text.AppendLine("Recover missing or damaged sheets from a known good backup; Restore as New Book lets you inspect a separate copy.");
			text.AppendLine("For invalid text, keep a backup outside the book folder, correct the reported characters and save as UTF-8.");
			text.AppendLine("Use ChordBook's editor to Save, or review external edits with Review Folder Changes. Then choose Validate Again.");
			text.AppendLine("Archived and recovery sheets are checked too; review their sheet type before repairing or removing them.");
		}

		return text.ToString();
	}

	#endregion

	#region Private Methods

	private static string Describe(BookValidationIssueKind kind) => kind switch
	{
		BookValidationIssueKind.InvalidSheetContent => "Invalid sheet content",
		BookValidationIssueKind.InvalidDatabase => "Unreadable or invalid book",
		BookValidationIssueKind.MissingAsset => "Missing sheet",
		BookValidationIssueKind.UnexpectedManagedAsset => "Unreferenced sheet",
		BookValidationIssueKind.PathMismatch => "Renamed or duplicate sheet",
		BookValidationIssueKind.LengthMismatch => "Changed sheet size",
		BookValidationIssueKind.HashMismatch => "Changed or damaged sheet",
		BookValidationIssueKind.UnreadableAsset => "Unreadable sheet",
		BookValidationIssueKind.DuplicateAssetIdentity => "Duplicate sheet identity",
		_ => kind.ToString(),
	};

	#endregion
}