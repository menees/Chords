using System.Globalization;
using System.Text;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui;

/// <summary>Shows saved catalog details without opening a sheet or changing the editor draft.</summary>
public sealed partial class SongDetailsPage : ContentPage
{
	public SongDetailsPage(BookSession session, Guid songId, Guid? fileId)
	{
		this.InitializeComponent();
		this.report.Text = GetSummary(session, songId, fileId);
	}

	private static string GetSummary(BookSession session, Guid songId, Guid? fileId)
	{
		var database = session.Database ?? throw new InvalidOperationException("No book is open.");
		var song = database.Songs.Single(item => item.Id == songId);
		static string Format(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "Unknown";

		StringBuilder summary = new();
		summary.AppendLine(song.Title);
		if (song.Artists.Count > 0)
		{
			summary.AppendLine(string.Join(", ", song.Artists));
		}

		DateTimeOffset? accessed = session.GetSongLastAccessed(songId) ?? song.LastAccessedUtc;
		summary.AppendLine().Append("Created: ").AppendLine(Format(SongDates.CreatedUtc(songId)));
		summary.Append("Modified: ").AppendLine(Format(song.Revision.ModifiedUtc == default ? null : song.Revision.ModifiedUtc));
		summary.Append("Last accessed on this device: ").AppendLine(Format(accessed));
		var sheet = database.SongFiles.FirstOrDefault(file => file.Id == fileId);
		if (sheet is not null)
		{
			string name = sheet.RelativePath.Replace($" [{sheet.Id:D}]", string.Empty, StringComparison.OrdinalIgnoreCase);
			summary.Append("Sheet: ").AppendLine(name);
			summary.Append("Sheet modified: ").AppendLine(Format(sheet.Revision.ModifiedUtc == default ? null : sheet.Revision.ModifiedUtc));
		}

		string[] lists = [.. database.Setlists.Where(list => list.Entries.Any(entry => entry.SongId == songId))
			.OrderBy(list => list.Name, StringComparer.CurrentCultureIgnoreCase)
			.Select(list => list.IsArchived ? list.Name + " (archived)" : list.Name)];
		summary.AppendLine().AppendLine("Setlists");
		summary.AppendLine(lists.Length == 0 ? "None" : string.Join(Environment.NewLine, lists));
		if (song.SourceMetadata.Count > 0)
		{
			summary.AppendLine().AppendLine("Source metadata");
			foreach (var (name, values) in song.SourceMetadata)
			{
				foreach (var value in values)
				{
					summary.Append(name).Append(": ").AppendLine(value.Value);
				}
			}
		}

		return summary.ToString();
	}

	private async void HandleBack(object? sender, EventArgs e)
		=> await this.Navigation.PopAsync().ConfigureAwait(true);
}
