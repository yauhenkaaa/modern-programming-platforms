namespace myThreadPool;

public enum PoolMonitorKind
{
    WorkerSpawned,
    WorkerExitedIdle,
    WorkerCrashed,
    WorkerReplaced,
    WorkEnqueued,
    WorkStarted,
    WorkCompleted,
    WorkFailed,
    StuckWorkerSuspected,
    QueueDepthChanged,
    ShutdownStarted,
    ShutdownCompleted
}

public sealed class PoolMonitorEventArgs : EventArgs
{
    public PoolMonitorKind Kind { get; init; }
    public DateTime UtcTime { get; init; }
    public int LiveWorkers { get; init; }
    public int QueueDepth { get; init; }
    public int? WorkerId { get; init; }
    public string? Message { get; init; }
    public Exception? Error { get; init; }
}
