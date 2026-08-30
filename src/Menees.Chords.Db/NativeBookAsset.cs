#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Provides repeatable stream access to one asset for a native-book write.</summary>
public sealed class NativeBookAsset
{
	#region Private Data

	private readonly Func<CancellationToken, Task<Stream>> openReadAsync;

	#endregion

	#region Constructors

	/// <summary>Initializes a new instance of the <see cref="NativeBookAsset"/> class.</summary>
	/// <param name="songFileId">The matching <see cref="SongFile"/> identifier.</param>
	/// <param name="openReadAsync">A function that opens a new readable stream owned by the writer.</param>
	public NativeBookAsset(Guid songFileId, Func<CancellationToken, Task<Stream>> openReadAsync)
	{
		ArgumentNullException.ThrowIfNull(openReadAsync);
		this.SongFileId = songFileId;
		this.openReadAsync = openReadAsync;
	}

	#endregion

	#region Public API

	/// <summary>Gets the matching <see cref="SongFile"/> identifier.</summary>
	public Guid SongFileId { get; }

	/// <summary>Opens a new readable stream. The caller owns the returned stream.</summary>
	public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		=> this.openReadAsync(cancellationToken);

	#endregion
}
