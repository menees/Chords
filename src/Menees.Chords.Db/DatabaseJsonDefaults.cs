using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Menees.Chords.Db;

/// <summary>Omits constructor defaults without losing explicit false/zero/empty overrides.</summary>
internal static class DatabaseJsonDefaults
{
	public static void Configure(JsonTypeInfo type)
	{
		if (type.Kind == JsonTypeInfoKind.Object && type.Type.Namespace == typeof(ChordDatabase).Namespace)
		{
			object? defaults = type.CreateObject?.Invoke();
			foreach (JsonPropertyInfo property in type.Properties)
			{
				if (property.Get is not null && property.Set is not null
					&& !(type.Type == typeof(ChordDatabase) && property.Name == "schemaVersion"))
				{
					object? baseline = defaults is not null ? property.Get(defaults)
						: property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
					Func<object, object?, bool>? previous = property.ShouldSerialize;
					property.ShouldSerialize = (owner, value) =>
						(previous is null || previous(owner, value)) && !Equivalent(value, baseline, type.Options);
				}
			}
		}
	}

	private static bool Equivalent(object? value, object? baseline, JsonSerializerOptions options)
	{
		bool result = Equals(value, baseline);
		if (!result && value is not null && baseline is not null && value.GetType() == baseline.GetType())
		{
			if (value is ICollection items && baseline is ICollection defaultItems)
			{
				// Most model collections default empty: compare counts without enumerating their content.
				result = items.Count == defaultItems.Count;
				if (result && items.Count > 0)
				{
					IEnumerator defaults = defaultItems.GetEnumerator();
					using IDisposable? disposable = defaults as IDisposable;
					foreach (object? item in items)
					{
						if (!defaults.MoveNext() || !Equivalent(item, defaults.Current, options))
						{
							result = false;
							break;
						}
					}
				}
			}
			else if (value.GetType().Namespace == typeof(ChordDatabase).Namespace)
			{
				JsonTypeInfo type = options.GetTypeInfo(value.GetType());
				result = type.Kind == JsonTypeInfoKind.Object;
				if (result)
				{
					foreach (JsonPropertyInfo property in type.Properties)
					{
						if (property.Get is not null && !Equivalent(property.Get(value), property.Get(baseline), options))
						{
							result = false;
							break;
						}
					}
				}
			}
		}

		return result;
	}
}
