using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RT950Update.ViewModels;

namespace RT950Update.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseFile_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select BTF File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            };

            var result = await StorageProvider.OpenFilePickerAsync(filePickerOptions);

            if (result != null && result.Count > 0)
            {
                var file = result[0];
                string? filePath = null;

                // Try to get local path
                if (file.TryGetLocalPath() != null)
                {
                    filePath = file.TryGetLocalPath();
                }
                else if (file.Path != null)
                {
                    filePath = file.Path.LocalPath;
                }
                else if (file.Path != null)
                {
                    filePath = file.Path.AbsolutePath;
                }

                if (!string.IsNullOrEmpty(filePath) && DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.FilePath = filePath;
                    viewModel.StatusMessage = $"File selected: {filePath}\n";
                }
                else
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.StatusMessage = "Error: Could not get file path\n";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.StatusMessage = $"Error selecting file: {ex.Message}\n";
            }
        }
    }

    private void ComPort_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RefreshPortsCommand.Execute(System.Reactive.Unit.Default);
        }
    }
}