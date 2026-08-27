namespace Menees.Chords.Sync;

public sealed class CloudTransportNotConfiguredException : InvalidOperationException
{
	public CloudTransportNotConfiguredException(string provider)
		: base($"The {provider} transport and authentication have not been configured.")
	{
	}
}
