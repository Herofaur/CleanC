using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CleanC.App.Services;
using CleanC.App.ViewModels;
using CleanC.Core.Formatting;
using CleanC.Core.Models;
using Microsoft.Win32;

namespace CleanC.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly CleanupService _cleanupService = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 WizTree 导出的 CSV 文件",
            Filter = "WizTree CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = File.Exists(_viewModel.CsvPath) ? Path.GetDirectoryName(_viewModel.CsvPath) : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.CsvPath = dialog.FileName;
        }
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ImportCsvAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法导入 CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => _viewModel.CancelScan();

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法定位项目", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectGroup_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedCleanupGroup?.SelectAll(true);

    private void ClearGroup_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedCleanupGroup?.SelectAll(false);

    private async void CleanSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.GetSelectedCleanupItems();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要清理的文件。", "没有选择文件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var totalSize = selected.Sum(item => item.Size);
        var confirmRequired = selected.Count(item => item.Level == CleanupLevel.ConfirmRequired);
        var warning = confirmRequired > 0
            ? $"\n\n其中 {confirmRequired:N0} 个项目属于“需要确认”，可能包含个人文件。"
            : string.Empty;
        var confirmation = MessageBox.Show(
            this,
            $"将永久删除 {selected.Count:N0} 个文件（{SizeFormatter.Format(totalSize)}）。{warning}\n\n删除后无法从回收站恢复，是否继续？",
            "确认永久删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.SetCleanupStatus("正在永久删除所选文件…");
        var result = await _cleanupService.DeletePermanentlyAsync(
            selected.Select(item => new CleanupRequest(item.FullPath, item.Size)));
        _viewModel.RemoveProcessedCleanupItems(selected.Select(item => item.FullPath));
        _viewModel.SetCleanupStatus($"清理完成 · 成功 {result.SucceededPaths.Count:N0} 个，失败 {result.Failures.Count:N0} 个");

        var message = $"已永久删除 {result.SucceededPaths.Count:N0} 个文件。";
        if (result.Failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine, result.Failures.Take(5).Select(failure => $"• {failure.Path}\n  {failure.Message}"));
            message += $"\n\n有 {result.Failures.Count:N0} 个文件未能处理：\n{details}" +
                       "\n\n本次选择已从当前列表隐藏；重新导入 CSV 可再次查看未删除的文件。";
        }

        MessageBox.Show(this, message, "清理结果", MessageBoxButton.OK,
            result.Failures.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _viewModel.CancelScan();
}
