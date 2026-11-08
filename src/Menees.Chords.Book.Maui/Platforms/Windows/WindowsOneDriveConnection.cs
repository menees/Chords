#region Using Directives

using System.IO;
using Menees.Chords.Sync;
using Menees.Chords.Sync.OneDrive;
using Microsoft.Identity.Client;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Owns device-local OneDrive registration and protected sign-in. Books contain no credentials.</summary>
internal sealed partial class WindowsOneDriveConnection : ICloudSyncConnection
{
	#region Private Data

	private const int TransferConcurrency = 4;
	private const string ClientPreference = "ChordBook.OneDrive.ClientId";
	private const string AccountPreference = "ChordBook.OneDrive.AccountId";
	private WindowsOneDriveTokenProvider? tokens;

	#endregion

	#region Public Properties

	public static string LocalDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menees", "ChordBook", "OneDrive");

	public static string ClientId => WindowsOneDriveTokenProvider.ApplicationClientId;

	public string ProviderName => "OneDrive";

	public string AccountLabel => "Microsoft account";

	public bool IsConnected => this.tokens?.AccountId is not null;

	public string StateDirectory => LocalDirectory;

	public int MaximumConcurrentTransfers => TransferConcurrency;

	public string AccountName => this.tokens?.AccountName ?? "Not connected";

	#endregion

	#region Public Methods

	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		this.tokens ??= await WindowsOneDriveTokenProvider.CreateAsync(
			ClientId,
			Path.Combine(LocalDirectory, "Credentials"),
			GetSavedAccount(),
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Explicit sync can silently reuse credentials or open sign-in when interaction is needed.</summary>
	public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
	{
		await this.LoadAsync(cancellationToken).ConfigureAwait(false);
		bool signIn = this.tokens!.AccountId is null;
		if (!signIn)
		{
			try
			{
				_ = await this.tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (InvalidOperationException exception) when (exception.InnerException is MsalUiRequiredException)
			{
				signIn = true;
			}
		}

		if (signIn)
		{
			await this.ConnectAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		WindowsOneDriveTokenProvider next = await WindowsOneDriveTokenProvider.CreateAsync(
			ClientId,
			Path.Combine(LocalDirectory, "Credentials"),
			GetSavedAccount(),
			cancellationToken).ConfigureAwait(false);
		try
		{
			await next.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
			Preferences.Default.Set(ClientPreference, ClientId);
			Preferences.Default.Set(AccountPreference, next.AccountId);
		}
		catch
		{
			next.Dispose();
			throw;
		}

		this.tokens?.Dispose();
		this.tokens = next;
	}

	public async Task DisconnectAsync(CancellationToken cancellationToken)
	{
		if (this.tokens is not null)
		{
			await this.tokens.DisconnectAsync(cancellationToken).ConfigureAwait(false);
			this.tokens.Dispose();
			this.tokens = null;
		}

		Preferences.Default.Remove(AccountPreference);
	}

	public async Task<ICloudReplica> OpenBookAsync(Guid bookId, CancellationToken cancellationToken)
	{
		WindowsOneDriveTokenProvider selected = this.RequireTokens();
		using OneDriveBookFolders folders = new(selected.AccountId!, selected);
		string folderId = await folders.EnsureAsync(bookId, cancellationToken).ConfigureAwait(false);
		return this.CreateTransport(folderId);
	}

	public async Task<IReadOnlyList<CloudBookChoice>> ListBooksAsync(CancellationToken cancellationToken)
	{
		WindowsOneDriveTokenProvider selected = this.RequireTokens();
		using OneDriveBookFolders folders = new(selected.AccountId!, selected);
		IReadOnlyDictionary<Guid, string> locations = await folders.ListAsync(cancellationToken).ConfigureAwait(false);
		List<CloudBookChoice> result = [];
		foreach ((Guid bookId, string folderId) in locations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using OneDriveHttpTransport replica = this.CreateTransport(folderId);
			try
			{
				// Discovery uses a separate owner so it cannot resume or clear an interrupted sync.
				await using CloudBookLease read = await CloudBookLease.AcquireAsync(
					replica, Guid.NewGuid(), Environment.MachineName, cancellationToken: cancellationToken).ConfigureAwait(false);
				CloudBookSnapshot? snapshot = await CloudBookReader.ReadAsync(read, cancellationToken).ConfigureAwait(false);
				if (snapshot is not null)
				{
					Db.ChordDatabase database = snapshot.Database;
					if (database.Id != bookId)
					{
						throw new InvalidDataException("A cloud book does not match its folder identity.");
					}

					result.Add(new(bookId, folderId, database.Name, database.Songs.Count));
				}
			}
			catch (CloudBookLockedException)
			{
				result.Add(new(bookId, folderId, "Locked OneDrive book " + bookId.ToString("D"), -1));
			}
		}

		return [.. result.OrderBy(book => book.Name, StringComparer.CurrentCultureIgnoreCase)];
	}

	public ICloudReplica OpenFolder(string folderId) => this.CreateTransport(folderId);

	public void Dispose() => this.tokens?.Dispose();

	#endregion

	#region Private Methods

	private static string? GetSavedAccount()
		=> Preferences.Default.Get(ClientPreference, ClientId) == ClientId
			? Preferences.Default.Get<string?>(AccountPreference, null) : null;

	private OneDriveHttpTransport CreateTransport(string folderId)
	{
		WindowsOneDriveTokenProvider selected = this.RequireTokens();
		return new(new(ClientId, selected.AccountId!, folderId), selected);
	}

	private WindowsOneDriveTokenProvider RequireTokens()
		=> this.tokens?.AccountId is not null ? this.tokens
			: throw new InvalidOperationException("Connect OneDrive before syncing or downloading a book.");

	#endregion
}
