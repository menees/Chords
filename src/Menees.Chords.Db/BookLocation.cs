namespace Menees.Chords.Db;

/// <summary>Identifies a book without exposing the store-specific location representation.</summary>
public sealed class BookLocation : IEquatable<BookLocation>
{
	internal BookLocation(Guid storeId, Guid token)
	{
		this.StoreId = storeId;
		this.Token = token;
	}

	internal Guid StoreId { get; }

	internal Guid Token { get; }

	/// <inheritdoc/>
	public bool Equals(BookLocation? other) => other is not null && this.StoreId == other.StoreId && this.Token == other.Token;

	/// <inheritdoc/>
	public override bool Equals(object? obj) => this.Equals(obj as BookLocation);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(this.StoreId, this.Token);

	/// <inheritdoc/>
	public override string ToString() => "Opaque book location";
}
