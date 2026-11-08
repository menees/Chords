using System.Text;

namespace Menees.Chords.Sync;

/// <summary>Provider-neutral, human-readable change lists for sync review and successful completion.</summary>
public static class BookSyncChangeReport
{
	public static string Format(BookSyncChanges changes, bool completed)
	{
		ArgumentNullException.ThrowIfNull(changes);
		StringBuilder result = new();
		void Sheets(string heading, IReadOnlyList<SyncSheetDetail> items)
		{
			result.AppendLine().Append(heading).Append(" (").Append(items.Count).AppendLine("):");
			foreach (SyncSheetDetail item in items.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(item => string.Join(", ", item.Artists), StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				result.Append("  • ").Append(item.Title);
				if (item.Artists.Count > 0)
				{
					result.Append(" — ").AppendJoin(", ", item.Artists);
				}

				result.Append(" · ").Append(item.Kind).Append(": ").AppendLine(item.Name);
			}
		}

		void Setlists(string heading, IReadOnlyList<SyncSetlistDetail> items)
		{
			result.AppendLine().Append(heading).Append(" (").Append(items.Count).AppendLine("):");
			foreach (SyncSetlistDetail item in items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				result.Append("  • ").AppendLine(item.Name);
			}
		}

		Sheets(completed ? "Uploaded sheets" : "Sheets to upload", changes.UploadedSheets);
		Sheets(completed ? "Downloaded sheets" : "Sheets to download", changes.DownloadedSheets);
		Setlists(completed ? "Uploaded setlists" : "Setlists to upload", changes.UploadedSetlists);
		Setlists(completed ? "Downloaded setlists" : "Setlists to download", changes.DownloadedSetlists);
		if (changes.RemovedLocalSetlists.Count > 0)
		{
			Setlists(completed ? "Setlists removed from this device" : "Setlists to remove from this device", changes.RemovedLocalSetlists);
		}

		if (changes.RemovedCloudSetlists.Count > 0)
		{
			Setlists(completed ? "Setlists removed from cloud" : "Setlists to remove from cloud", changes.RemovedCloudSetlists);
		}

		return result.ToString().TrimEnd();
	}
}
