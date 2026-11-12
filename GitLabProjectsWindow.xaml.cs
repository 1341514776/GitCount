using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using GitCount.Models;
using GitCount.Services;
using GitCount.ViewModels;

namespace GitCount;

public partial class GitLabProjectsWindow : Window
{
    private readonly GitLabService _gitLabService = new();
    private readonly ObservableCollection<GitLabProjectSelectionRow> _rows = [];
    private readonly HashSet<long> _existingProjectIds;
    private ICollectionView? _view;
    private bool _loaded;
    private bool _busy;

    public GitLabProjectsWindow(
        string? initialBaseUrl,
        string? initialToken,
        IReadOnlyCollection<long> existingProjectIds)
    {
        InitializeComponent();
        BaseUrlBox.Text = initialBaseUrl ?? string.Empty;
        TokenBox.Password = initialToken ?? string.Empty;
        _existingProjectIds = existingProjectIds.ToHashSet();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterProject;
        ProjectGrid.ItemsSource = _view;
        UpdateSelectedCount();
    }

    public string BaseUrl { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;
    public string ConnectedUserName { get; private set; } = string.Empty;
    public IReadOnlyList<GitLabProjectInfo> SelectedProjects { get; private set; } = [];

    private async void LoadProjects_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var baseUrl = BaseUrlBox.Text.Trim();
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            SetStatus("请填写 GitLab 根地址和访问令牌。", isError: true);
            return;
        }

        SetBusy(true);
        SetStatus("正在连接 GitLab 并加载项目...", isError: false);

        try
        {
            var connection = await _gitLabService.TestConnectionAsync(baseUrl, token);
            var projects = await _gitLabService.GetProjectsAsync(baseUrl, token);

            foreach (var row in _rows)
            {
                row.PropertyChanged -= Row_PropertyChanged;
            }

            _rows.Clear();
            foreach (var project in projects)
            {
                var row = new GitLabProjectSelectionRow(project, _existingProjectIds.Contains(project.Id));
                row.PropertyChanged += Row_PropertyChanged;
                _rows.Add(row);
            }

            _view?.Refresh();
            EmptyOverlay.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.IsEnabled = _rows.Count > 0;
            _loaded = true;

            var versionText = string.IsNullOrWhiteSpace(connection.Version)
                ? string.Empty
                : $" · GitLab {connection.Version}";
            var name = string.IsNullOrWhiteSpace(connection.DisplayName)
                ? connection.Username
                : $"{connection.DisplayName} ({connection.Username})";
            ConnectedUserName = string.IsNullOrWhiteSpace(name) ? connection.Username : name;
            SetStatus($"连接成功：{name}{versionText}，已加载 {_rows.Count} 个项目。", isError: false);
            UpdateSelectedCount();
        }
        catch (Exception exception)
        {
            _loaded = false;
            SaveButton.IsEnabled = false;
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        _view?.Refresh();

    private void SelectVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null)
        {
            return;
        }

        foreach (var row in _view.Cast<GitLabProjectSelectionRow>())
        {
            row.IsSelected = true;
        }
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            SetStatus("请先连接 GitLab 并加载项目。", isError: true);
            return;
        }

        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Project).ToList();
        if (selected.Count == 0)
        {
            SetStatus("请至少选择一个需要统计的项目。", isError: true);
            return;
        }

        BaseUrl = _gitLabService.NormalizeBaseUrl(BaseUrlBox.Text);
        Token = TokenBox.Password.Trim();
        SelectedProjects = selected;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GitLabProjectSelectionRow.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    private bool FilterProject(object item)
    {
        if (item is not GitLabProjectSelectionRow row)
        {
            return false;
        }

        var search = SearchBox?.Text?.Trim() ?? string.Empty;
        return search.Length == 0
            || row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || row.PathWithNamespace.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateSelectedCount()
    {
        SelectedCountText.Text = $"已选择 {_rows.Count(row => row.IsSelected)} / {_rows.Count}";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        LoadButton.IsEnabled = !busy;
        BaseUrlBox.IsEnabled = !busy;
        TokenBox.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        ProjectGrid.IsEnabled = !busy;
    }

    private void SetStatus(string text, bool isError)
    {
        ConnectionStatusText.Text = text;
        ConnectionStatusText.Foreground = isError
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
    }
}
