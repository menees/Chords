using System.Numerics;

namespace Menees.Chords.Book.Maui;

/// <summary>Column widths shared by a catalog header and all realized rows.</summary>
public sealed class CatalogColumnLayout
{
	internal static readonly CatalogColumn[] ColumnOrder =
	[
		CatalogColumn.None, CatalogColumn.Artist, CatalogColumn.Key, CatalogColumn.Tempo, CatalogColumn.Capo,
		CatalogColumn.Tags, CatalogColumn.Songs, CatalogColumn.Date, CatalogColumn.Duration,
	];

	internal static readonly CatalogColumnLayout Empty = new(CatalogColumn.None, Array.ConvertAll(ColumnOrder, _ => new GridLength(0)));

	private CatalogColumnLayout(CatalogColumn fields, GridLength[] widths)
	{
		this.Fields = fields;
		this.Widths = widths;
	}

	internal CatalogColumn Fields { get; }

	internal GridLength[] Widths { get; }

	internal static int IndexOf(CatalogColumn field)
		=> field == CatalogColumn.None ? 0 : BitOperations.TrailingZeroCount((uint)field) + 1;

	internal static bool IsByline(CatalogColumn fields, CatalogColumn field)
		=> (fields & CatalogColumn.Compact) != 0 && field is CatalogColumn.Artist or CatalogColumn.Tags;

	internal static CatalogColumnLayout Create(
		CatalogColumn fields, ReadOnlySpan<double> natural, ReadOnlySpan<double> headers, double compactTitle, double available)
	{
		GridLength[] widths = new GridLength[ColumnOrder.Length];
		Array.Fill(widths, new GridLength(0));
		double preferred = 0;
		double minimum = 0;
		for (int index = 0; index < ColumnOrder.Length; index++)
		{
			CatalogColumn field = ColumnOrder[index];
			if (index == 0 || ((fields & field) != 0 && !IsByline(fields, field)))
			{
				double value = index == 0 && (fields & CatalogColumn.Compact) != 0
					? Math.Max(natural[index], compactTitle) : natural[index];
				double width = Math.Max(headers[index], value);
				widths[index] = new(width);
				preferred += width;
				minimum += headers[index];
			}
		}

		if (preferred > available)
		{
			double compression = preferred > minimum ? Math.Max(0, (available - minimum) / (preferred - minimum)) : 0;
			double headerScale = minimum > available ? available / minimum : 1;
			for (int index = 0; index < widths.Length; index++)
			{
				if (widths[index].Value > 0)
				{
					double heading = headers[index];
					widths[index] = new((heading * headerScale) + ((widths[index].Value - heading) * compression));
				}
			}
		}

		return new(fields, widths);
	}
}
