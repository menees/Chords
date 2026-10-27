#region Using Directives

using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Owns disk snapshots for one read-only import review. Dispose to discard its temporary files.</summary>
public sealed class BookImportReview : IDisposable
{
	#region Private Data

	private const int BufferSize = 81920;
	private readonly List<BookImportItem> orderedItems = [];
	private readonly ReadOnlyCollection<BookImportItem> itemView;
	private readonly string directory = Path.Combine(Path.GetTempPath(), "ChordBookImportReview-" + Guid.NewGuid().ToString("N"));
	private readonly Dictionary<Guid, BookImportItem> items = [];
	private bool disposed;

	#endregion

	#region Constructors

	internal BookImportReview(string expectedJson, Guid bookId)
	{
		this.ExpectedCatalog = expectedJson;
		this.itemView = this.orderedItems.AsReadOnly();
		this.BookId = bookId;
		Directory.CreateDirectory(this.directory);
	}

	#endregion

	#region Public API

	/// <summary>Gets the reviewed book identity.</summary>
	public Guid BookId { get; }

	/// <summary>Gets sources in selection order, without retaining their bytes in memory.</summary>
	public IReadOnlyList<BookImportItem> Items
	{
		get
		{
			ObjectDisposedException.ThrowIf(this.disposed, this);
			return this.itemView;
		}
	}

	/// <summary>Gets the exact catalog snapshot used to reject stale review actions.</summary>
	public string ExpectedCatalog { get; }

	/// <summary>Opens and verifies a captured source without reading the original selected file again.</summary>
	public async Task<Stream> OpenSourceAsync(Guid itemId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		BookImportItem item = this.items[itemId];
		Stream stream = this.OpenCapturedSource(itemId);
		try
		{
			string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
			if (!StringComparer.OrdinalIgnoreCase.Equals(item.ContentHash, hash))
			{
				throw new BookStoreValidationException("An import snapshot changed. Close and reopen Import.");
			}

			stream.Position = 0;
			return stream;
		}
		catch
		{
			await stream.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>Opens a fresh read-only captured stream for a viewer. Apply independently verifies its hash.</summary>
	public Stream OpenCapturedSource(Guid itemId)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		if (!this.items.ContainsKey(itemId))
		{
			throw new ArgumentException("Choose a source in this import review.", nameof(itemId));
		}

		return new FileStream(
			this.GetPath(itemId),
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			BufferSize,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
	}

	/// <summary>Removes only this review's private scratch files, leaving the book and selected sources untouched.</summary>
	public void Dispose()
	{
		if (!this.disposed)
		{
			foreach (string path in Directory.EnumerateFiles(this.directory))
			{
				File.Delete(path);
			}

			Directory.Delete(this.directory);
			this.disposed = true;
		}
	}

	internal string GetPath(Guid id) => Path.Combine(this.directory, id.ToString("N"));

	internal void Add(BookImportItem item)
	{
		this.items.Add(item.Id, item);
		this.orderedItems.Add(item);
	}
	#endregion
}