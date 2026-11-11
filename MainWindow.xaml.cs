using System.IO;
using System.Windows;
using GitCount.ViewModels;
using Microsoft.Win32;

namespace GitCount;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择 Git 仓库文件夹",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await AddProjectPathsAsync(dialog.FolderNames);
    }

    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProjectItemViewModel project })
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"确定从列表中移除「{project.Name}」吗？\n\n只会移除统计配置，不会删除本地文件。",
            "移除项目",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            ViewModel.RemoveProject(project);
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllProjectsIncluded(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllProjectsIncluded(false);

    private async void StartStatistics_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CalculateAsync();
    }

    private void CancelStatistics_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCalculation();
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExport)
        {
            MessageBox.Show(this, "还没有可导出的统计结果。", "导出 CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出统计结果",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"GitCount_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await ViewModel.ExportCsvAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsBusy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var folders = paths
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await AddProjectPathsAsync(folders);
    }

    private async Task AddProjectPathsAsync(IEnumerable<string> paths)
    {
        var errors = new List<string>();

        foreach (var path in paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var error = await ViewModel.AddProjectAsync(path);
            if (!string.IsNullOrWhiteSpace(error))
            {
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine + Environment.NewLine, errors.Distinct()),
                "无法添加项目",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.CancelCalculation();
        ViewModel.SaveState();
    }
}
