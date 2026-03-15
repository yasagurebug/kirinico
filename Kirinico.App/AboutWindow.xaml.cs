using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Kirinico.App;

public partial class AboutWindow : Window
{
    public AboutWindow(object dataContext)
    {
        InitializeComponent();
        DataContext = dataContext;
    }

    private void RepositoryLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true,
        });
        e.Handled = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
