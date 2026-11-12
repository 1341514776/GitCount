using System.Windows;
using System.Windows.Input;
using GitCount.ViewModels;

namespace GitCount;

public partial class CommitDetailsWindow : Window
{
    public CommitDetailsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F11)
        {
            e.Handled = true;
            Close();
        }
    }
}
