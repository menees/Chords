using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Db;

/// <summary>Renames legacy sync assets and updates their catalog paths in one local transaction.</summary>
public static class LegacySheetNames
{
	/// <summary>Removes legacy sync tokens from managed assets without changing their contents or identities.</summary>
	public static async Task<int> CleanAsync(IBookStore store, BookLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		string expected = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase book = DatabaseJson.Deserialize(expected);
		var (_, renamed) = await CleanCoreAsync(store, location, book, expected, cancellationToken).ConfigureAwait(false);
		return renamed;
	}

	internal static async Task<(string Catalog, int Renamed)> CleanCoreAsync(
		IBookStore store, BookLocation location, ChordDatabase book, string expected, CancellationToken cancellationToken)
	{
		var changes = book.SongFiles.Select(file => (File: file, Name: PortableManagedFileName.WithoutLegacySyncToken(file.RelativePath, file.Id)))
			.Where(change => change.File.RelativePath != change.Name).ToArray();
		int renamed = 0;
		if (changes.Length > 0)
		{
			HashSet<string> occupied = new(
				store is FileSystemBookStore filesystem
					? Directory.EnumerateFileSystemEntries(filesystem.GetDirectory(location)).Select(path => Path.GetFileName(path)!)
					: book.SongFiles.Select(file => file.RelativePath),
				PortableManagedFileName.Comparer);
			await using IStagedBookWrite write = await store.StageWriteAsync(location, expected, cancellationToken).ConfigureAwait(false);
			foreach (var (file, name) in changes.Where(change => occupied.Contains(change.File.RelativePath)))
			{
				string available = PortableManagedFileName.ChooseAvailable(name, file.Id, occupied.Contains);
				await write.RenameManagedAssetAsync(file.Id, available, cancellationToken).ConfigureAwait(false);
				occupied.Add(available);
				file.RelativePath = available;
				renamed++;
			}

			if (renamed > 0)
			{
				expected = DatabaseJson.Serialize(book);
				await write.WriteDatabaseJsonAsync(expected, cancellationToken).ConfigureAwait(false);
				await write.CommitAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		return (expected, renamed);
	}
}
