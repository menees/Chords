#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Stages one bound source into durable, verified device-local storage.</summary>
internal static class SyncAssetStager
{
	#region Public Methods

	public static async Task StageAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		SyncTransferAsset asset,
		string path,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await using Stream source = asset.LocalSourceId != Guid.Empty
			? await store.OpenManagedAssetAsync(location, asset.LocalSourceId, cancellationToken).ConfigureAwait(false)
			: await replica.DownloadAsync(new(asset.SourceItem!.Id), cancellationToken).ConfigureAwait(false);
		await using Stream verified = await VerifiedAssetStage.CreateAsync(
			source, asset.ContentHash, asset.Length, Path.GetDirectoryName(path)!, cancellationToken).ConfigureAwait(false);
		string temporary = path + ".tmp";
		try
		{
			await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await verified.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
				output.Flush(flushToDisk: true);
			}

			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporary, path, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	#endregion
}
