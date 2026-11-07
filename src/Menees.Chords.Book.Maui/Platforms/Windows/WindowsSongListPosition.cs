#region Using Directives

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;
using MauiElement = Microsoft.Maui.Controls.Element;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

/// <summary>Retains a browser viewport through hidden item-source refreshes without restoring into a newer view.</summary>
internal sealed class WindowsSongListPosition
{
	#region Private Data

	private const int MaximumAttempts = 6;
	private const double OffsetTolerance = 0.5;
	private (Guid? SongId, double Y, double Offset)? position;
	private int generation;
	private FrameworkElement? pendingElement;
	private EventHandler<object>? layoutUpdated;
	private RoutedEventHandler? loaded;

	#endregion

	#region Public Methods

	public void Capture(MauiCollectionView view, bool onlyIfEmpty = false)
	{
		if (!onlyIfEmpty || this.position is null)
		{
			this.Cancel();
			if (IsVisible(view) && view.Handler?.PlatformView is FrameworkElement root && FindScrollViewer(root) is ScrollViewer scroll)
			{
				(SongRow? row, double y) = GetVisibleRow(root, scroll);
				this.position = (row?.Id, y, scroll.VerticalOffset);
			}
		}
	}

	public void Cancel(bool forgetPosition = true)
	{
		this.generation++;
		if (this.pendingElement is FrameworkElement element)
		{
			element.LayoutUpdated -= this.layoutUpdated;
			element.Loaded -= this.loaded;
		}

		this.pendingElement = null;
		this.layoutUpdated = null;
		this.loaded = null;
		if (forgetPosition)
		{
			this.position = null;
		}
	}

	public void Restore(MauiCollectionView view)
	{
		if (this.position is { } saved && view.Handler?.PlatformView is FrameworkElement root)
		{
			this.Cancel(forgetPosition: false);
			int version = this.generation;
			(SongRow? item, SongGroup? group) = FindSong(view, saved.SongId);
			int attempts = 0;
			bool queued = false;
			void Queue()
			{
				if (!queued && version == this.generation && IsVisible(view))
				{
					queued = true;
					if (!root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
					{
						queued = false;
						if (version == this.generation && IsVisible(view) && FindScrollViewer(root) is ScrollViewer scroll
							&& root.ActualWidth > 0 && root.ActualHeight > 0 && scroll.ViewportHeight > 0
											&& (scroll.ExtentHeight > 0 || view.ItemsSource is System.Collections.ICollection { Count: 0 }))
						{
							(ListViewItem? container, double y) = FindRow(root, scroll, saved.SongId);
							double target = Math.Clamp(container is null ? saved.Offset : scroll.VerticalOffset + y - saved.Y, 0, scroll.ScrollableHeight);
							if (((container is not null || item is null) && Math.Abs(scroll.VerticalOffset - target) <= OffsetTolerance)
								|| attempts++ >= MaximumAttempts)
							{
								this.Cancel();
							}
							else if (container is null && item is not null)
							{
								view.ScrollTo(item, group, ScrollToPosition.Start, animate: false);
							}
							else
							{
								_ = scroll.ChangeView(null, target, null, disableAnimation: true);
							}
						}
					}))
					{
						this.Cancel();
					}
				}
			}

			this.pendingElement = root;
			this.layoutUpdated = (_, _) => Queue();
			this.loaded = (_, _) => Queue();
			root.LayoutUpdated += this.layoutUpdated;
			root.Loaded += this.loaded;
		}
	}

	#endregion

	#region Private Methods

	private static (SongRow? Song, SongGroup? Group) FindSong(MauiCollectionView view, Guid? id)
	{
		SongRow? song = null;
		SongGroup? group = null;
		if (view.ItemsSource is IEnumerable<SongGroup> groups)
		{
			foreach (SongGroup candidate in groups)
			{
				song = candidate.FirstOrDefault(row => row.Id == id);
				if (song is not null)
				{
					group = candidate;
					break;
				}
			}
		}
		else if (view.ItemsSource is IEnumerable<SongRow> rows)
		{
			song = rows.FirstOrDefault(row => row.Id == id);
		}

		return (song, group);
	}

	private static (SongRow? Row, double Y) GetVisibleRow(FrameworkElement root, ScrollViewer scroll)
	{
		SongRow? result = null;
		double y = 0;
		foreach (ListViewItem container in Rows(root))
		{
			double top = container.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
			if (container.Content is Microsoft.Maui.Controls.Platform.ItemTemplateContext { Item: SongRow row }
				&& top + container.ActualHeight > 0 && top < scroll.ViewportHeight && (result is null || top < y))
			{
				result = row;
				y = top;
			}
		}

		return (result, y);
	}

	private static (ListViewItem? Row, double Y) FindRow(FrameworkElement root, ScrollViewer scroll, Guid? id)
	{
		ListViewItem? container = Rows(root).FirstOrDefault(row =>
			row.Content is Microsoft.Maui.Controls.Platform.ItemTemplateContext { Item: SongRow song } && song.Id == id);
		return (container, container?.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y ?? 0);
	}

	private static IEnumerable<ListViewItem> Rows(DependencyObject parent)
	{
		if (parent is ListViewItem row)
		{
			yield return row;
		}
		else
		{
			for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
			{
				foreach (ListViewItem child in Rows(VisualTreeHelper.GetChild(parent, index)))
				{
					yield return child;
				}
			}
		}
	}

	private static bool IsVisible(MauiCollectionView view)
	{
		bool result = true;
		for (MauiElement? element = view; result && element is not null; element = element.Parent)
		{
			result = element is not VisualElement { IsVisible: false }
				&& !(element is ContentPage page && page.Navigation.NavigationStack is { Count: > 0 } stack && stack[^1] != page);
		}

		return result;
	}

	private static ScrollViewer? FindScrollViewer(DependencyObject parent)
	{
		ScrollViewer? result = parent as ScrollViewer;
		for (int index = 0; result is null && index < VisualTreeHelper.GetChildrenCount(parent); index++)
		{
			result = FindScrollViewer(VisualTreeHelper.GetChild(parent, index));
		}

		return result;
	}

	#endregion
}
