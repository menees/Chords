#region Using Directives

using System.IO;
using Menees.Chords.Sync.OneDrive;
using Microsoft.Identity.Client;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Owns device-local OneDrive registration and protected sign-in. Books contain no credentials.</summary>
internal sealed partial class WindowsOneDriveConnection : IDisposable
{
	#region Private Data

	private const string ClientPreference = "ChordBook.OneDrive.ClientId";
	private const string AccountPreference = "ChordBook.OneDrive.AccountId";
	private WindowsOneDriveTokenProvider? tokens;

	#endregion

	#region Public Properties

	public static string LocalDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menees", "ChordBook", "OneDrive");

	public static string ClientId => WindowsOneDriveTokenProvider.ApplicationClientId;

	public string? AccountId => this.tokens?.AccountId;

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

	public async Task<OneDriveHttpTransport> OpenBookAsync(Guid bookId, CancellationToken cancellationToken)
	{
		WindowsOneDriveTokenProvider selected = this.RequireTokens();
		using OneDriveBookFolders folders = new(selected.AccountId!, selected);
		string folderId = await folders.EnsureAsync(bookId, cancellationToken).ConfigureAwait(false);
		return this.OpenFolder(folderId);
	}

	public async Task<IReadOnlyList<OneDriveBookChoice>> ListBooksAsync(CancellationToken cancellationToken)
	{
		WindowsOneDriveTokenProvider selected = this.RequireTokens();
		using OneDriveBookFolders folders = new(selected.AccountId!, selected);
		IReadOnlyDictionary<Guid, string> locations = await folders.ListAsync(cancellationToken).ConfigureAwait(false);
		List<OneDriveBookChoice> result = [];
		foreach ((Guid bookId, string folderId) in locations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using OneDriveHttpTransport replica = this.OpenFolder(folderId);
			Sync.CloudBookSnapshot? snapshot = await Sync.CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
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

		return [.. result.OrderBy(book => book.Name, StringComparer.CurrentCultureIgnoreCase)];
	}

	public OneDriveHttpTransport OpenFolder(string folderId)
	{
		WindowsOneDriveTokenProvider selected = this.RequireTokens();
		return new(new(ClientId, selected.AccountId!, folderId), selected);
	}

	public void Dispose() => this.tokens?.Dispose();

	#endregion

	#region Private Methods

	private static string? GetSavedAccount()
		=> Preferences.Default.Get(ClientPreference, ClientId) == ClientId
			? Preferences.Default.Get<string?>(AccountPreference, null) : null;

	private WindowsOneDriveTokenProvider RequireTokens()
		=> this.tokens?.AccountId is not null ? this.tokens
			: throw new InvalidOperationException("Connect OneDrive before syncing or downloading a book.");

	#endregion
}
