using Menees.Chords.Book.Maui.Platforms.Windows;

string cacheDirectory = Path.Combine(args.Single(), "protected-cache");
using var provider = await WindowsOneDriveTokenProvider.CreateAsync(Guid.NewGuid().ToString("D"), cacheDirectory, null);
if (provider.AccountId is not null) throw new Exception("An unselected account must not be inferred from another connection.");
try
{
	await provider.GetAccessTokenAsync(CancellationToken.None);
	throw new Exception("A signed-out provider returned a token.");
}
catch (InvalidOperationException error) when (error.Message.Contains("Connect this OneDrive account")) { }
await provider.DisconnectAsync(CancellationToken.None);
using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
	await WindowsOneDriveTokenProvider.CreateAsync(Guid.NewGuid().ToString("D"), cacheDirectory, null, canceled.Token);
	throw new Exception("Canceled initialization succeeded.");
}
catch (OperationCanceledException) { }
try
{
	await WindowsOneDriveTokenProvider.CreateAsync("not-an-application-id", cacheDirectory, null);
	throw new Exception("Invalid client ID succeeded.");
}
catch (ArgumentException) { }
Console.WriteLine("PASS: Windows protected-cache verification, signed-out silent-token refusal, disconnect, cancellation and client-ID validation.");
Console.WriteLine("No browser was launched; no live account or book was used.");
