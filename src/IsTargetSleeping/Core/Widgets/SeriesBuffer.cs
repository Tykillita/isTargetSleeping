namespace IsTargetSleeping;

/// <summary>Buffer circular pequeño para gráficas en vivo.</summary>
public sealed class SeriesBuffer(int capacity = 60)
{
    private readonly double[] values = new double[Math.Max(1, capacity)];
    private int start, count;

    public int Count => count;
    public void Add(double value)
    {
        values[(start + count) % values.Length] = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        if (count < values.Length) count++;
        else start = (start + 1) % values.Length;
    }
    public double[] Snapshot() => Enumerable.Range(0, count).Select(i => values[(start + i) % values.Length]).ToArray();
}

public static class SystemMetricCatalog
{
    public static readonly IReadOnlyList<string> All = ["cpu", "ram", "gpu", "disk", "battery", "network"];
    public static readonly IReadOnlyList<string> Defaults = ["cpu", "ram"];
    public static List<string> Normalize(IEnumerable<string>? values)
    {
        var selected = (values ?? Defaults).Where(All.Contains).Distinct().Take(2).ToList();
        if (selected.Count == 0) selected = Defaults.ToList();
        foreach (var id in Defaults) if (selected.Count < 2 && !selected.Contains(id)) selected.Add(id);
        return selected;
    }
    public static List<string> Toggle(IReadOnlyList<string> selected, string id)
    {
        var ids = Normalize(selected);
        if (!All.Contains(id)) return ids;
        if (ids.Contains(id))
        {
            ids.Remove(id);
            ids.Add(All.First(x => !ids.Contains(x) && x != id));
            return ids;
        }
        return ids.Count < 2 ? [.. ids, id] : ids.ToList();
    }
}
