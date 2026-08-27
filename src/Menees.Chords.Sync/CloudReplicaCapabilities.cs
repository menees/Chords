namespace Menees.Chords.Sync;

[Flags]
public enum CloudReplicaCapabilities
{
	None = 0,
	ChangeTokens = 1,
	Rename = 2,
	ConditionalMutation = 4,
}
