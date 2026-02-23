using System;
using System.Collections.Generic;
using System.Linq;
using Krypton_Desktop.Models;

namespace Krypton_Desktop.Services;

/// <summary>
/// Manages the local clipboard history with a configurable limit.
/// </summary>
public class ClipboardHistoryService
{
    private readonly object _lock = new();
    private readonly List<ClipboardItem> _items = [];
    private int _maxItems = 100;

    public event EventHandler<ClipboardItem>? ItemAdded;
    public event EventHandler? HistoryCleared;

    public int MaxItems
    {
        get => _maxItems;
        set
        {
            _maxItems = Math.Max(1, value);
            TrimHistory();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Adds an item to the history. If an item with the same hash exists, it's moved to the top.
    /// </summary>
    public void Add(ClipboardItem item)
    {
        lock (_lock)
        {
            // Check for duplicate by hash
            var existing = _items.FindIndex(i => i.ContentHash == item.ContentHash);
            if (existing >= 0)
            {
                // Move existing item to top
                var existingItem = _items[existing];
                _items.RemoveAt(existing);
                existingItem.CreatedAt = DateTime.UtcNow;
                _items.Insert(0, existingItem);
                ItemAdded?.Invoke(this, existingItem);
                return;
            }

            // Add new item at the beginning
            _items.Insert(0, item);
            TrimHistory();
        }

        ItemAdded?.Invoke(this, item);
    }

    /// <summary>
    /// Gets all items in the history, newest first.
    /// </summary>
    public IReadOnlyList<ClipboardItem> GetAll()
    {
        lock (_lock)
        {
            return _items.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets the most recent item, or null if empty.
    /// </summary>
    public ClipboardItem? GetMostRecent()
    {
        lock (_lock)
        {
            return _items.Count > 0 ? _items[0] : null;
        }
    }

    /// <summary>
    /// Moves an item to the top of the history.
    /// </summary>
    public void MoveToTop(Guid itemId)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == itemId);
            if (index > 0)
            {
                var item = _items[index];
                _items.RemoveAt(index);
                item.CreatedAt = DateTime.UtcNow;
                _items.Insert(0, item);
            }
        }
    }

    /// <summary>
    /// Removes an item from the history.
    /// </summary>
    public bool Remove(Guid itemId)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == itemId);
            if (index >= 0)
            {
                _items.RemoveAt(index);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Clears all items from the history.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
        HistoryCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Searches the history for items containing the query.
    /// </summary>
    public IReadOnlyList<ClipboardItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll();

        var lowerQuery = query.ToLowerInvariant();

        lock (_lock)
        {
            return _items
                .Where(i => i.Preview.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Loads history from server entries.
    /// </summary>
    public void LoadFromServer(IEnumerable<ClipboardItem> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
            {
                var existing = _items.FindIndex(i => i.ContentHash == item.ContentHash);
                if (existing >= 0)
                {
                    // Update existing with server info
                    _items[existing].ServerId = item.ServerId;
                    _items[existing].IsSynced = true;
                }
                else
                {
                    _items.Add(item);
                }
            }

            // Sort by date descending (newest first)
            _items.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            TrimHistory();
        }
    }

    /// <summary>
    /// Adds a single item from server (broadcast).
    /// </summary>
    public void AddFromServer(ClipboardItem item)
    {
        lock (_lock)
        {
            // Check for duplicate by hash
            var existing = _items.FindIndex(i => i.ContentHash == item.ContentHash);
            if (existing >= 0)
            {
                // Move to top and mark as synced
                var existingItem = _items[existing];
                _items.RemoveAt(existing);
                existingItem.IsSynced = true;
                _items.Insert(0, existingItem);
                ItemAdded?.Invoke(this, existingItem);
                return;
            }

            // Add new item at the beginning
            item.IsSynced = true;
            _items.Insert(0, item);
            TrimHistory();
        }

        ItemAdded?.Invoke(this, item);
    }

    private void TrimHistory()
    {
        while (_items.Count > _maxItems)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }
}
