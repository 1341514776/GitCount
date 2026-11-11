using System.IO;
using System.Windows;

namespace GitCount;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            var details = args.Exception.ToString();
            var logPath = Path.Combine(AppContext.BaseDirectory, "GitCount-error.log");
            try
            {
                File.WriteAllText(logPath, details);
            }
            catch
            {
                // Logging must never hide the original error.
            }

            MessageBox.Show(
                $"GitCount 遇到未处理的错误：{args.Exception.Message}\n\n详细信息已写入：{logPath}",
                "GitCount 发生错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}

