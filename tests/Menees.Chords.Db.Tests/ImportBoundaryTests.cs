#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class ImportBoundaryTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow(0)]
	[DataRow(5000)]
	public async Task BinaryControlsAreRejectedWithoutChangingTheBook(int offset)
	{
		var token = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		Guid device = Guid.NewGuid();
		BookLocation location = await store.CreateBookAsync("Boundary", device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		byte[] bytes = Encoding.UTF8.GetBytes(new string('x', offset) + "MZ\0\u0001");
		using MemoryStream source = new(bytes);
		await Should.ThrowAsync<BookStoreValidationException>(() => BookImportService.ImportAsync(
			store, location, "not-a-song.exe", source, device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		(await store.EnumerateManagedAssetsAsync(location, token).ToListAsync(token)).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task PrefixProbeAcceptsPartialUnicodeAndPlainLyrics()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Unicode", device, token);
		const int PrefixOffset = 4095;
		string text = new string('x', PrefixOffset) + "\u00e9 \U0001f3b8\r\nPlain lyrics\t\f";
		foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(false, true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 })
		{
			byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
			using MemoryStream source = new(bytes);
			BookImportResult result = await BookImportService.ImportAsync(store, location, "lyrics", source, device, token);
			await using Stream saved = await store.OpenManagedAssetAsync(location, result.SongFileId, token);
			using MemoryStream copy = new();
			await saved.CopyToAsync(copy, token);
			copy.ToArray().ShouldBe(bytes);
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task LargePdfUsesBoundedReadsAndDiskStaging()
	{
		var token = this.TestContext.CancellationToken;
		string directory = Path.Combine(Path.GetTempPath(), "ChordPdfImport-" + Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(directory);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("PDF", device, token);
			const long PdfLength = 32L * 1024 * 1024;
			using GeneratedPdf source = new(PdfLength);
			long allocated = GC.GetTotalAllocatedBytes(precise: true);
			BookImportResult imported = await BookImportService.ImportAsync(store, location, "scan.pdf", source, device, token);
			(GC.GetTotalAllocatedBytes(precise: true) - allocated).ShouldBeLessThan(PdfLength);
			await using Stream saved = await store.OpenManagedAssetAsync(location, imported.SongFileId, token);
			saved.Length.ShouldBe(PdfLength);
			const int MaximumReadSize = 81920;
			source.MaximumRead.ShouldBeLessThanOrEqualTo(MaximumReadSize);
			ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
			database.SongFiles.Single().MediaKind.ShouldBe(MediaKind.Pdf);
			saved.ReadByte().ShouldBe((int)'%');
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[DoNotParallelize]
	public async Task InterruptedImportsLeaveNoAssetsOrTemporaryFiles(bool cancel)
	{
		var token = this.TestContext.CancellationToken;
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
		InMemoryBookStore store = new();
		Guid device = Guid.NewGuid();
		BookLocation location = await store.CreateBookAsync("Boundary", device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		string[] temporaryFiles = Directory.GetFiles(Path.GetTempPath(), "ChordBookImport-*.tmp");
		using GeneratedPdf source = new(32L * 1024 * 1024, pdf: cancel, onRead: position =>
		{
			if (cancel && position > 1024 * 1024)
			{
				cancellation.Cancel();
			}
		});
		Task Import() => BookImportService.ImportAsync(store, location, "song", source, device, cancellation.Token);
		if (cancel)
		{
			await Should.ThrowAsync<OperationCanceledException>(Import);
		}
		else
		{
			await Should.ThrowAsync<BookStoreValidationException>(Import);
		}

		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		(await store.EnumerateManagedAssetsAsync(location, token).ToListAsync(token)).ShouldBeEmpty();
		Directory.GetFiles(Path.GetTempPath(), "ChordBookImport-*.tmp").ShouldBe(temporaryFiles);
		source.Position.ShouldBeLessThan(source.Length);
		source.CanRead.ShouldBeTrue();
	}

	#endregion

	#region Private Types

	private sealed class GeneratedPdf(long length, bool pdf = true, Action<long>? onRead = null) : Stream
	{
		private long position;

		public int MaximumRead { get; private set; }

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => length;

		public override long Position { get => this.position; set => throw new NotSupportedException(); }

		public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

		public override int Read(Span<byte> buffer)
		{
			this.MaximumRead = Math.Max(this.MaximumRead, buffer.Length);
			int count = (int)Math.Min(buffer.Length, length - this.position);
			buffer[..count].Fill((byte)'x');
			ReadOnlySpan<byte> header = "%PDF-"u8;
			for (int index = 0; index < count && pdf && this.position + index < header.Length; index++)
			{
				buffer[index] = header[(int)this.position + index];
			}

			this.position += count;
			return count;
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			onRead?.Invoke(this.position);
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult(this.Read(buffer.Span));
		}

		public override void Flush() => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	#endregion
}
