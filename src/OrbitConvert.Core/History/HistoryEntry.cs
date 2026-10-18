namespace OrbitConvert.Core.History;

public sealed record HistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public required IReadOnlyList<string> Inputs { get; init; }
    public required string TargetKey { get; init; }
    public required string TargetLabel { get; init; }
    public required bool Succeeded { get; init; }
    public string Status { get; init; } = "";
    public IReadOnlyList<string> Outputs { get; init; } = [];
    public string? Error { get; init; }
    public string? Note { get; init; }
    public double DurationSeconds { get; init; }

    public string InputSummary => Inputs.Count switch
    {
        0 => "(none)",
        1 => Path.GetFileName(Inputs[0]),
        _ => $"{Path.GetFileName(Inputs[0])} + {Inputs.Count - 1} more",
    };
}

public interface IHistoryStore
{
    IReadOnlyList<HistoryEntry> Entries { get; }
    event EventHandler? Changed;
    void Add(HistoryEntry entry);
    void Remove(Guid id);
    void Clear();
}
