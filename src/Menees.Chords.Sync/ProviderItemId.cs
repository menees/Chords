namespace Menees.Chords.Sync;

public readonly struct ProviderItemId : IEquatable<ProviderItemId>
{
	public ProviderItemId(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		this.Value = value;
	}

	public string Value { get; }

	public static bool operator ==(ProviderItemId left, ProviderItemId right) => left.Equals(right);

	public static bool operator !=(ProviderItemId left, ProviderItemId right) => !left.Equals(right);

	public bool Equals(ProviderItemId other) => StringComparer.Ordinal.Equals(this.Value, other.Value);

	public override bool Equals(object? obj) => obj is ProviderItemId other && this.Equals(other);

	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Value);

	public override string ToString() => this.Value;
}
