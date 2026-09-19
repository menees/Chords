#region Using Directives

using Menees.Chords.Sync.OneDrive;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Explicit system-browser sign-in with a Windows-protected, app-local MSAL cache.</summary>
/// <remarks>The caller owns the lifetime and must finish pending operations before disposing.</remarks>
internal sealed partial class WindowsOneDriveTokenProvider : IOneDriveTokenProvider, IDisposable
{
	#region Private Data

	private static readonly string[] Scopes = ["https://graph.microsoft.com/Files.ReadWrite.AppFolder"];
	private readonly IPublicClientApplication client;
	private readonly MsalCacheHelper cache;
	private readonly string? expectedAccountId;
	private readonly SemaphoreSlim gate = new(1, 1);
	private IAccount? account;
	private bool disposed;

	#endregion

	#region Constructors

	private WindowsOneDriveTokenProvider(IPublicClientApplication client, MsalCacheHelper cache, string? expectedAccountId)
	{
		this.client = client;
		this.cache = cache;
		this.expectedAccountId = expectedAccountId;
	}

	#endregion

	#region Public API

	public string? AccountId => this.account?.HomeAccountId.Identifier;

	/// <summary>Loads the selected cached account without network access or interactive sign-in.</summary>
	public static async Task<WindowsOneDriveTokenProvider> CreateAsync(
		string clientId, string cacheDirectory, string? expectedAccountId, CancellationToken cancellationToken = default)
	{
		if (!Guid.TryParse(clientId, out Guid applicationId) || applicationId == Guid.Empty)
		{
			throw new ArgumentException("Enter the public application (client) ID from the ChordBook Entra registration.", nameof(clientId));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
		cancellationToken.ThrowIfCancellationRequested();
		IPublicClientApplication client = PublicClientApplicationBuilder.Create(applicationId.ToString("D"))
			.WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
			.WithRedirectUri("http://localhost")
			.Build();
		StorageCreationProperties storage = new StorageCreationPropertiesBuilder("onedrive-" + applicationId.ToString("N") + ".cache", cacheDirectory)
			.Build();
		MsalCacheHelper cache = await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);

		// No plaintext fallback. A protection failure prevents connecting this account.
		cache.VerifyPersistence();
		cache.RegisterCache(client.UserTokenCache);
		WindowsOneDriveTokenProvider result = new(client, cache, expectedAccountId);
		try
		{
			if (!string.IsNullOrWhiteSpace(expectedAccountId))
			{
				result.account = await client.GetAccountAsync(expectedAccountId).ConfigureAwait(false);
			}

			cancellationToken.ThrowIfCancellationRequested();
		}
		catch
		{
			result.Dispose();
			throw;
		}

		return result;
	}

	/// <summary>Called only by an explicit Connect/Reconnect action. Never by comparison or transfer code.</summary>
	public async Task AuthenticateAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			AuthenticationResult result = await this.client.AcquireTokenInteractive(Scopes)
				.WithUseEmbeddedWebView(false)
				.WithPrompt(Prompt.SelectAccount)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false);
			if (this.expectedAccountId is not null && result.Account.HomeAccountId.Identifier != this.expectedAccountId)
			{
				throw new InvalidOperationException("That account differs from this replica's configured OneDrive account. "
					+ "Reconnect with the original account.");
			}

			this.account = result.Account;
		}
		finally
		{
			this.gate.Release();
		}
	}

	public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			IAccount selected = this.account ?? throw new InvalidOperationException("Connect this OneDrive account before syncing.");
			AuthenticationResult result = await this.client.AcquireTokenSilent(Scopes, selected).ExecuteAsync(cancellationToken).ConfigureAwait(false);
			if (result.Account.HomeAccountId.Identifier != selected.HomeAccountId.Identifier)
			{
				throw new InvalidOperationException("OneDrive returned credentials for a different account. Reconnect before syncing.");
			}

			return result.AccessToken;
		}
		catch (MsalUiRequiredException)
		{
			throw new InvalidOperationException("OneDrive sign-in needs attention. Use Reconnect, then start sync again.");
		}
		finally
		{
			this.gate.Release();
		}
	}

	public async Task DisconnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (this.account is IAccount selected)
			{
				await this.client.RemoveAsync(selected).ConfigureAwait(false);
				this.account = null;
			}
		}
		finally
		{
			this.gate.Release();
		}
	}

	public void Dispose()
	{
		if (!this.disposed)
		{
			this.disposed = true;
			this.cache.UnregisterCache(this.client.UserTokenCache);
			this.gate.Dispose();
		}
	}

	#endregion
}
