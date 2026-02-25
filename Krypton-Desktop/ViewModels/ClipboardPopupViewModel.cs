using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using Krypton_Desktop.Models;
using Krypton_Desktop.Services;
using ReactiveUI;

namespace Krypton_Desktop.ViewModels;

public class ClipboardPopupViewModel : ViewModelBase
{
    private readonly ClipboardHistoryService _historyService;
    private readonly ClipboardMonitorService _clipboardMonitor;
    private readonly ServerConnectionService? _serverConnection;
    private readonly Action _closeAction;
    private string _searchQuery = string.Empty;
    private ClipboardItemViewModel? _selectedItem;
    private bool _isSearchingServer;
    private bool _showingServerResults;
    private bool _isLoadingMore;
    private bool _hasMoreServerEntries;
    private bool _reachedEnd;
    private int _serverOffset;

    public ObservableCollection<ClipboardItemViewModel> Items { get; } = [];

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            FilterItems();
        }
    }

    public ClipboardItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    public bool IsSearchingServer
    {
        get => _isSearchingServer;
        set => this.RaiseAndSetIfChanged(ref _isSearchingServer, value);
    }

    public bool ShowingServerResults
    {
        get => _showingServerResults;
        set => this.RaiseAndSetIfChanged(ref _showingServerResults, value);
    }

    public bool CanSearchServer => _serverConnection?.IsAuthenticated == true;

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set => this.RaiseAndSetIfChanged(ref _isLoadingMore, value);
    }

    public bool HasMoreServerEntries
    {
        get => _hasMoreServerEntries;
        set => this.RaiseAndSetIfChanged(ref _hasMoreServerEntries, value);
    }

    public bool ReachedEnd
    {
        get => _reachedEnd;
        set => this.RaiseAndSetIfChanged(ref _reachedEnd, value);
    }

    public ReactiveCommand<Unit, Unit> ClearHistoryCommand { get; }
    public ReactiveCommand<ClipboardItemViewModel, Unit> PasteItemCommand { get; }
    public ReactiveCommand<ClipboardItemViewModel, Unit> DeleteItemCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> SearchServerCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowLocalCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadMoreCommand { get; }

    public ClipboardPopupViewModel(
        ClipboardHistoryService historyService,
        ClipboardMonitorService clipboardMonitor,
        ServerConnectionService? serverConnection,
        Action closeAction)
    {
        _historyService = historyService;
        _clipboardMonitor = clipboardMonitor;
        _serverConnection = serverConnection;
        _closeAction = closeAction;

        ClearHistoryCommand = ReactiveCommand.Create(ClearHistory);
        PasteItemCommand = ReactiveCommand.CreateFromTask<ClipboardItemViewModel>(PasteItemAsync);
        DeleteItemCommand = ReactiveCommand.Create<ClipboardItemViewModel>(DeleteItem);
        CloseCommand = ReactiveCommand.Create(Close);
        SearchServerCommand = ReactiveCommand.CreateFromTask(SearchServerAsync);
        ShowLocalCommand = ReactiveCommand.Create(ShowLocal);
        LoadMoreCommand = ReactiveCommand.CreateFromTask(LoadMoreFromServerAsync);

        // Subscribe to history changes
        _historyService.ItemAdded += OnItemAdded;
        _historyService.HistoryCleared += OnHistoryCleared;

        // Load initial items
        RefreshItems();
    }

    public void RefreshItems()
    {
        Items.Clear();
        var items = string.IsNullOrWhiteSpace(_searchQuery)
            ? _historyService.GetAll()
            : _historyService.Search(_searchQuery);

        foreach (var item in items)
        {
            Items.Add(new ClipboardItemViewModel(item));
        }
    }

    private void FilterItems()
    {
        if (!ShowingServerResults)
        {
            RefreshItems();
        }
    }

    private async Task SearchServerAsync()
    {
        if (_serverConnection == null || !_serverConnection.IsAuthenticated)
            return;

        IsSearchingServer = true;
        _serverOffset = 0;
        HasMoreServerEntries = false;
        ReachedEnd = false;

        try
        {
            var query = string.IsNullOrWhiteSpace(_searchQuery) ? "" : _searchQuery;

            if (string.IsNullOrEmpty(query))
            {
                // Use paginated pull for browsing all entries
                var result = await _serverConnection.PullHistoryAsync(50, 0);
                if (result != null)
                {
                    Items.Clear();
                    foreach (var entry in result.Value.Entries)
                    {
                        var item = ClipboardItem.FromProto(entry);
                        Items.Add(new ClipboardItemViewModel(item));
                    }
                    _serverOffset = result.Value.Entries.Length;
                    HasMoreServerEntries = result.Value.HasMore;
                    ReachedEnd = !result.Value.HasMore;
                    ShowingServerResults = true;
                }
            }
            else
            {
                // Use search for queries
                var results = await _serverConnection.SearchAsync(query, 100);
                if (results != null)
                {
                    Items.Clear();
                    foreach (var entry in results)
                    {
                        var item = ClipboardItem.FromProto(entry);
                        Items.Add(new ClipboardItemViewModel(item));
                    }
                    ReachedEnd = true;
                    ShowingServerResults = true;
                }
            }
        }
        finally
        {
            IsSearchingServer = false;
        }
    }

    private async Task LoadMoreFromServerAsync()
    {
        if (_serverConnection == null || !_serverConnection.IsAuthenticated)
            return;

        if (IsLoadingMore || !HasMoreServerEntries)
            return;

        IsLoadingMore = true;

        try
        {
            var result = await _serverConnection.PullHistoryAsync(50, _serverOffset);
            if (result != null)
            {
                foreach (var entry in result.Value.Entries)
                {
                    var item = ClipboardItem.FromProto(entry);
                    Items.Add(new ClipboardItemViewModel(item));
                }
                _serverOffset += result.Value.Entries.Length;
                HasMoreServerEntries = result.Value.HasMore;
                ReachedEnd = !result.Value.HasMore;
            }
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void ShowLocal()
    {
        ShowingServerResults = false;
        HasMoreServerEntries = false;
        ReachedEnd = false;
        _serverOffset = 0;
        RefreshItems();
    }

    private void OnItemAdded(object? sender, ClipboardItem item)
    {
        // Refresh on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshItems);
    }

    private void OnHistoryCleared(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Items.Clear());
    }

    private void ClearHistory()
    {
        _historyService.Clear();
    }

    private async Task PasteItemAsync(ClipboardItemViewModel itemVm)
    {
        if (itemVm.Item.ContentType == Krypton.Shared.Protocol.ClipboardContentType.Image)
        {
            await _clipboardMonitor.SetImageAsync(itemVm.Item.Content);
            _historyService.MoveToTop(itemVm.Item.Id);
            _closeAction();
            await Task.Delay(100);
            SimulateCtrlV();
            return;
        }

        if (itemVm.Item.TextContent != null)
        {
            await _clipboardMonitor.SetTextAsync(itemVm.Item.TextContent);
            _historyService.MoveToTop(itemVm.Item.Id);
        }
        _closeAction();

        // Wait briefly for window to close and focus to return
        await Task.Delay(100);

        // Simulate Ctrl+V to paste
        SimulateCtrlV();
    }

    private static void SimulateCtrlV()
    {
        if (OperatingSystem.IsWindows())
        {
            SimulateCtrlVWindows();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static void SimulateCtrlVWindows()
    {
        // Press Ctrl
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        // Press V
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        // Release V
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        // Release Ctrl
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void DeleteItem(ClipboardItemViewModel itemVm)
    {
        _historyService.Remove(itemVm.Item.Id);
        Items.Remove(itemVm);
        itemVm.Dispose();
    }

    private void Close()
    {
        _closeAction();
    }

    public void Cleanup()
    {
        _historyService.ItemAdded -= OnItemAdded;
        _historyService.HistoryCleared -= OnHistoryCleared;
        foreach (var item in Items)
            item.Dispose();
    }
}

public class ClipboardItemViewModel : ViewModelBase, IDisposable
{
    public ClipboardItem Item { get; }

    public string Preview => Item.Preview;
    public string TimeAgo => GetTimeAgo(Item.CreatedAt);
    public bool IsSynced => Item.IsSynced;

    public bool IsImage => Item.ContentType == Krypton.Shared.Protocol.ClipboardContentType.Image;
    public bool IsText => !IsImage;

    public string ContentTypeIcon => Item.ContentType switch
    {
        Krypton.Shared.Protocol.ClipboardContentType.Text => "ContentCopy",
        Krypton.Shared.Protocol.ClipboardContentType.Image => "Image",
        Krypton.Shared.Protocol.ClipboardContentType.File => "File",
        _ => "Help"
    };

    /// <summary>
    /// Dynamic preview lines based on content length.
    /// </summary>
    public int PreviewLines => Item.Preview.Length switch
    {
        < 50 => 1,
        < 150 => 2,
        < 300 => 3,
        _ => 4
    };

    private Avalonia.Media.Imaging.Bitmap? _imageThumbnail;

    /// <summary>
    /// Lazily decoded image thumbnail. Only populated for image items.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap? ImageThumbnail
    {
        get
        {
            if (_imageThumbnail != null || !IsImage || Item.Content.Length == 0)
                return _imageThumbnail;
            try
            {
                using var ms = new System.IO.MemoryStream(Item.Content);
                _imageThumbnail = new Avalonia.Media.Imaging.Bitmap(ms);
            }
            catch { /* corrupt image — leave null */ }
            return _imageThumbnail;
        }
    }

    public ClipboardItemViewModel(ClipboardItem item)
    {
        Item = item;
    }

    public void Dispose()
    {
        _imageThumbnail?.Dispose();
        _imageThumbnail = null;
    }

    private static string GetTimeAgo(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime;

        if (span.TotalSeconds < 60)
            return "just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";

        return dateTime.ToLocalTime().ToString("MMM d");
    }
}
