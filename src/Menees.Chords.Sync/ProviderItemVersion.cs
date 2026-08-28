namespace Menees.Chords.Sync;

public readonly struct ProviderItemVersion : IEquatable<ProviderItemVersion>
{
	public ProviderItemVersion(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		this.Value = value;
	}

	public string Value { get; }

	public static bool operator ==(ProviderItemVersion left, ProviderItemVersion right) => left.Equals(right);

	public static bool operator !=(ProviderItemVersion left, ProviderItemVersion right) => !left.Equals(right);

	public bool Equals(ProviderItemVersion other) => StringComparer.Ordinal.Equals(this.Value, other.Value);

	public override bool Equals(object? obj) => obj is ProviderItemVersion other && this.Equals(other);

	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Value);

	public override string ToString() => this.Value;
}
