#region Using Directives

using System.Diagnostics;
using Menees.Chords.Book.Maui.Platforms.Windows;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Shares column sizing between the header and the realized rows of a catalog list.</summary>
public sealed partial class CatalogGrid : Grid
{
	#region Data

	public static readonly BindableProperty ColumnLayoutProperty = BindableProperty.Create(
		nameof(ColumnLayout),
		typeof(CatalogColumnLayout),
		typeof(CatalogGrid),
		CatalogColumnLayout.Empty,
		propertyChanged: (bindable, previous, _) => ((CatalogGrid)bindable).UpdateColumns(((CatalogColumnLayout)previous).Fields));

	public static readonly BindableProperty FieldProperty = BindableProperty.CreateAttached(
		"Field", typeof(CatalogColumn), typeof(CatalogGrid), CatalogColumn.None);

	private CatalogColumn requestedFields;
	private double[]? headerWidths;
	private double[]? naturalWidths;
	private double compactTitleWidth;
	private CancellationTokenSource? measurement;

	#endregion

	#region Constructors

	public CatalogGrid()
	{
		const int CellInset = 4;
		this.Padding = new(CellInset);
		foreach (GridLength width in CatalogColumnLayout.Empty.Widths)
		{
			this.ColumnDefinitions.Add(new(width));
		}

		this.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)];
	}

	#endregion

	#region Public Members

	public CatalogColumnLayout ColumnLayout
	{
		get => (CatalogColumnLayout)this.GetValue(ColumnLayoutProperty);
		set => this.SetValue(ColumnLayoutProperty, value);
	}

	public CatalogColumn Fields => this.ColumnLayout.Fields;

	public bool IsHeader { get; set; }

	public bool CompactOnNarrow { get; set; }

	public static CatalogColumn GetField(BindableObject view) => (CatalogColumn)view.GetValue(FieldProperty);

	public static void SetField(BindableObject view, CatalogColumn value) => view.SetValue(FieldProperty, value);

	#endregion

	#region Sizing

	internal void SizeToContent(CatalogColumn fields, IEnumerable<CatalogCellContent> content, WindowsCatalogTextMeasure measure)
	{
		this.measurement?.Cancel();
		this.measurement?.Dispose();
		this.measurement = new();
		this.requestedFields = fields;
		_ = this.MeasureContentAsync(content, measure, this.measurement.Token);
	}

	internal void UpdateHeaderLayout()
	{
		const double CompactWidth = 640;
		if (this.headerWidths is not null)
		{
			CatalogColumn fields = this.requestedFields;
			if (this.CompactOnNarrow && this.Width is > 0 and < CompactWidth)
			{
				fields |= CatalogColumn.Compact;
			}

			double available = this.Width > 0 ? Math.Max(0, this.Width - this.Padding.HorizontalThickness) : double.PositiveInfinity;
			CatalogColumnLayout layout = CatalogColumnLayout.Create(
				fields, this.naturalWidths ?? this.headerWidths, this.headerWidths, this.compactTitleWidth, available);
			if (this.Fields != fields || !this.ColumnLayout.Widths.AsSpan().SequenceEqual(layout.Widths))
			{
				this.ColumnLayout = layout;
			}
		}
	}

	#endregion

	#region Layout

	protected override void OnChildAdded(Element child)
	{
		base.OnChildAdded(child);
		if (child is CatalogCell cell)
		{
			this.PlaceCell(cell);
		}
	}

	private async Task MeasureContentAsync(
		IEnumerable<CatalogCellContent> content, WindowsCatalogTextMeasure measure, CancellationToken cancellationToken)
	{
		const int SliceMilliseconds = 6;
		const int SliceCheckInterval = 32;
		measure.BeginPass();
		int columnCount = CatalogColumnLayout.ColumnOrder.Length;
		this.headerWidths ??= new double[columnCount];
		foreach (CatalogCell header in this.Children.OfType<CatalogCell>())
		{
			this.headerWidths[CatalogColumnLayout.IndexOf(GetField(header))] = measure.Measure(header.Text, header.FontSize, header.FontAttributes);
		}

		double[] widths = new double[columnCount];
		double bylineWidth = 0;
		int cellsMeasured = 0;
		bool provisionalApplied = false;
		long started = Stopwatch.GetTimestamp();
		foreach (CatalogCellContent cell in content)
		{
			if (!string.IsNullOrWhiteSpace(cell.Text))
			{
				int index = CatalogColumnLayout.IndexOf(cell.Field);
				int fontSize = cell.Secondary ? CatalogCell.BylineFontSize : CatalogCell.DefaultFontSize;
				FontAttributes font = cell.Archived ? FontAttributes.Italic : FontAttributes.None;
				widths[index] = Math.Max(widths[index], measure.Measure(cell.Text, fontSize, font));
				if (this.CompactOnNarrow && cell.Field is CatalogColumn.Artist or CatalogColumn.Tags)
				{
					bylineWidth = Math.Max(bylineWidth, measure.Measure(cell.Text, CatalogCell.BylineFontSize, FontAttributes.None));
				}
			}

			if (++cellsMeasured % SliceCheckInterval == 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= SliceMilliseconds)
			{
				if (!provisionalApplied)
				{
					this.UpdateHeaderLayout();
					provisionalApplied = true;
				}

				await Task.Yield();
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				started = Stopwatch.GetTimestamp();
			}
		}

		this.naturalWidths = widths;
		this.compactTitleWidth = bylineWidth;
		this.UpdateHeaderLayout();
	}

	private void UpdateColumns(CatalogColumn previousFields)
	{
		for (int index = 0; index < this.ColumnDefinitions.Count; index++)
		{
			this.ColumnDefinitions[index].Width = this.ColumnLayout.Widths[index];
		}

		if (this.Fields != previousFields)
		{
			foreach (CatalogCell cell in this.Children.OfType<CatalogCell>())
			{
				this.PlaceCell(cell);
			}
		}
	}

	private void PlaceCell(CatalogCell cell)
	{
		CatalogColumn field = GetField(cell);
		if (field != CatalogColumn.None)
		{
			bool byline = CatalogColumnLayout.IsByline(this.Fields, field);
			int column = CatalogColumnLayout.IndexOf(field);
			SetColumn((BindableObject)cell, byline ? 0 : column);
			SetRow((BindableObject)cell, byline ? (field == CatalogColumn.Artist ? 1 : 2) : 0);
			cell.IsByline = byline && !this.IsHeader;
			cell.ShowColumn = (this.Fields & field) != 0 && !(this.IsHeader && byline);
		}
	}

	#endregion
}
