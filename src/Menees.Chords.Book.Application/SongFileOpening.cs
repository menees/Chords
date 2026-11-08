#region Using Directives

using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>File opening reuses captured, bounded imports and never combines title/artist matches automatically.</summary>
public static class SongFileOpening
{
	#region Public Methods

	public static async Task<SongFileOpenResult> OpenAsync(
		BookApplicationSession session, string path, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		BookImportReview review = await session.ReviewImportAsync([path], cancellationToken).ConfigureAwait(false);
		try
		{
			session.ValidateImportReview(review);

			BookImportItem? item = review.Items.SingleOrDefault();
			BookImportCandidate[] matches = [.. (item?.Candidates ?? []).Where(candidate => !candidate.IsIncoming
				&& StringComparer.OrdinalIgnoreCase.Equals(candidate.ContentHash, item?.ContentHash))
				.OrderBy(candidate => candidate.IsArchived).ThenByDescending(candidate => candidate.IsDefault).DistinctBy(candidate => candidate.SongId)];
			SongFileOpenResult result;
			if (item is null)
			{
				result = new(null, null, false, [], review);
			}
			else if (matches.Length > 0)
			{
				BookImportCandidate? match = matches.Length == 1 ? matches[0] : null;
				result = new(match?.SongId, match?.SheetId, false, Array.AsReadOnly(matches), null);
			}
			else if (item.Conflict == BookImportConflict.NewSong)
			{
				BookImportResult imported = (await session.ApplyImportAsync(review, [item.DefaultDecision], deviceId, cancellationToken)
					.ConfigureAwait(false)).Single();
				result = new(imported.SongId, imported.SongFileId, true, [], null);
			}
			else
			{
				result = new(null, null, false, [], review);
			}

			if (result.Review is null)
			{
				review.Dispose();
			}

			return result;
		}
		catch
		{
			review.Dispose();
			throw;
		}
	}
	#endregion
}
