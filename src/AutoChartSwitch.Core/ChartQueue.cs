using System.Collections.ObjectModel;

namespace AutoChartSwitch.Core;

public interface IChartQueue
{
    ReadOnlyObservableCollection<ChartInfo> Items { get; }
    event EventHandler? Changed;
    void InsertFront(ChartInfo chart);
    void InsertBack(ChartInfo chart);
    bool Replace(Guid id, ChartInfo chart);
    bool Delete(Guid id);
    bool Move(Guid id, int targetIndex);
    ChartInfo? PeekFront();
    bool RemoveFront(Guid expectedId);
    void ReplaceAll(IEnumerable<ChartInfo> charts);
    void Append(IEnumerable<ChartInfo> charts);
}

public sealed class ChartQueue : IChartQueue
{
    private readonly ObservableCollection<ChartInfo> _items = [];
    public ReadOnlyObservableCollection<ChartInfo> Items { get; }
    public event EventHandler? Changed;

    public ChartQueue(IEnumerable<ChartInfo>? initial = null)
    {
        Items = new(_items);
        if (initial is not null)
            foreach (var chart in initial) _items.Add(chart);
    }

    public void InsertFront(ChartInfo chart) { _items.Insert(0, chart); OnChanged(); }
    public void InsertBack(ChartInfo chart) { _items.Add(chart); OnChanged(); }

    public bool Replace(Guid id, ChartInfo chart)
    {
        var index = IndexOf(id);
        if (index < 0) return false;
        _items[index] = chart with { Id = id };
        OnChanged();
        return true;
    }

    public bool Delete(Guid id)
    {
        var index = IndexOf(id);
        if (index < 0) return false;
        _items.RemoveAt(index);
        OnChanged();
        return true;
    }

    public bool Move(Guid id, int targetIndex)
    {
        var from = IndexOf(id);
        if (from < 0 || targetIndex < 0 || targetIndex >= _items.Count || from == targetIndex) return false;
        _items.Move(from, targetIndex);
        OnChanged();
        return true;
    }

    public ChartInfo? PeekFront() => _items.Count == 0 ? null : _items[0];

    public bool RemoveFront(Guid expectedId)
    {
        if (_items.Count == 0 || _items[0].Id != expectedId) return false;
        _items.RemoveAt(0);
        OnChanged();
        return true;
    }

    public void ReplaceAll(IEnumerable<ChartInfo> charts)
    {
        _items.Clear();
        foreach (var chart in charts) _items.Add(chart);
        OnChanged();
    }

    public void Append(IEnumerable<ChartInfo> charts)
    {
        foreach (var chart in charts) _items.Add(chart);
        OnChanged();
    }

    private int IndexOf(Guid id)
    {
        for (var i = 0; i < _items.Count; i++) if (_items[i].Id == id) return i;
        return -1;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
