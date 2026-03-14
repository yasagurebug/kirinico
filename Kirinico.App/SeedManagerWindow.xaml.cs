using Kirinico.App.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace Kirinico.App;

public partial class SeedManagerWindow : System.Windows.Window
{
    private readonly Action<OpenCvSharp.Point> _deleteSeed;

    public ObservableCollection<SeedPreviewItem> SeedItems { get; }

    public SeedManagerWindow(IEnumerable<SeedPreviewItem> seedItems, Action<OpenCvSharp.Point> deleteSeed)
    {
        _deleteSeed = deleteSeed;
        SeedItems = new ObservableCollection<SeedPreviewItem>(seedItems);
        InitializeComponent();
        SeedItemsControl.ItemsSource = SeedItems;
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SeedPreviewItem item })
        {
            return;
        }

        _deleteSeed(item.SeedPoint);
        SeedItems.Remove(item);

        if (SeedItems.Count == 0)
        {
            Close();
        }
    }
}
