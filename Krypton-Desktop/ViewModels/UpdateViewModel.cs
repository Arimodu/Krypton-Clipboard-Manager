using System;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Krypton_Desktop.Services;
using ReactiveUI;

namespace Krypton_Desktop.ViewModels;

public class UpdateViewModel : ViewModelBase
{
    private readonly UpdateService _updateService;
    private readonly string _downloadUrl;
    private readonly Action _closeAction;
    private readonly CancellationTokenSource _cts = new();
    private string? _downloadedFilePath;

    private double _downloadProgress;
    private bool _isDownloading;
    private bool _isComplete;
    private string _statusMessage = "Ready to download.";

    public string CurrentVersion { get; }
    public string LatestVersion { get; }
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public bool IsNotMacOS => !OperatingSystem.IsMacOS();

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDownloading, value);
            this.RaisePropertyChanged(nameof(IsProgressVisible));
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isComplete, value);
            this.RaisePropertyChanged(nameof(IsProgressVisible));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool IsProgressVisible => _isDownloading || _isComplete;

    public ReactiveCommand<Unit, Unit> DownloadCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public UpdateViewModel(string currentVersion, string latestVersion, string downloadUrl, Action closeAction)
    {
        CurrentVersion = currentVersion;
        LatestVersion = latestVersion;
        _downloadUrl = downloadUrl;
        _closeAction = closeAction;
        _updateService = new UpdateService();

        var canDownload = this.WhenAnyValue(
            x => x.IsDownloading, x => x.IsComplete,
            (downloading, complete) => !downloading && !complete);

        DownloadCommand = ReactiveCommand.CreateFromTask(DownloadAsync, canDownload);

        var canRestart = this.WhenAnyValue(x => x.IsComplete);
        RestartCommand = ReactiveCommand.CreateFromTask(RestartAsync, canRestart);

        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    private async Task DownloadAsync()
    {
        // On macOS, skip downloading and just open the releases page
        if (OperatingSystem.IsMacOS())
        {
            _updateService.OpenReleasesPage();
            _closeAction();
            return;
        }

        IsDownloading = true;
        StatusMessage = "Downloading...";

        try
        {
            var progress = new Progress<double>(p =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DownloadProgress = p));

            var ext = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            var tempPath = Path.Combine(Path.GetTempPath(), $"krypton-update{ext}");

            await _updateService.DownloadAsync(_downloadUrl, tempPath, progress, _cts.Token);
            _downloadedFilePath = tempPath;

            IsDownloading = false;
            IsComplete = true;
            StatusMessage = "Download complete. Click Restart to apply.";
        }
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            StatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            StatusMessage = $"Download failed: {ex.Message}";
        }
    }

    private async Task RestartAsync()
    {
        try
        {
            StatusMessage = "Applying update...";
            await _updateService.ApplyUpdateAsync(_downloadedFilePath!);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
        }
    }

    private void Cancel()
    {
        _cts.Cancel();
        _closeAction();
    }
}
