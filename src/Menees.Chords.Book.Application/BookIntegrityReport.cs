#region Using Directives

using System.Text;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Formats a read-only integrity check for display or a saved diagnostic report.</summary>
public static class BookIntegrityReport
{
	#region Public Methods

	/// <summary>Creates a human-readable report with suggested next steps and no source modifications.</summary>
	public static string Format(BookValidationReport report)
	{
		ArgumentNullException.ThrowIfNull(report);
		StringBuilder text = new();
		text.AppendLine("ChordBook Integrity Report");
		if (report.Database is ChordDatabase database)
		{
			text.AppendLine(database.Name);
			text.AppendLine($"Songs: {database.Songs.Count}; sheets: {database.SongFiles.Count}; setlists: {database.Setlists.Count}");
		}

		text.AppendLine();
		text.AppendLine(report.IsValid ? "No integrity problems found." : $"{report.Issues.Count} issue(s) found.");
		text.AppendLine("This check did not change any book files.");
		foreach (BookValidationIssue issue in report.Issues)
		{
			text.AppendLine();
			text.AppendLine($"{Describe(issue.Kind)}: {issue.RelativePath ?? "Book database"}");
			text.AppendLine(issue.Message);
		}

		if (!report.IsValid)
		{
			text.AppendLine();
			text.AppendLine("Next steps");
			text.AppendLine("Review Folder Changes can reconcile intended edits and renames.");
			text.AppendLine("Keep copies of duplicate or unreferenced sheets before deciding which to import or remove.");
			text.AppendLine("Recover missing or damaged sheets from a known good backup; Restore as New Book lets you inspect a separate copy.");
		}

		return text.ToString();
	}

	#endregion

	#region Private Methods

	private static string Describe(BookValidationIssueKind kind) => kind switch
	{
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