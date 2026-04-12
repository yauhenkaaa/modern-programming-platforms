using System.Collections.Generic;
using SysMonitor = System.Threading.Monitor;

namespace myThreadPool;

/// <summary>
/// Собственный пул потоков: очередь задач под <see cref="Monitor"/>, рабочие — явные <see cref="Thread"/>.
/// Без использования <see cref="System.Threading.ThreadPool"/> и <see cref="System.Threading.Tasks.Task.Run"/>.
/// </summary>
public sealed class DynamicThreadPool : IDisposable
{
    private readonly object _sync = new();
    private readonly DynamicThreadPoolOptions _options;
    private readonly Queue<WorkItem> _queue = new();
    private readonly List<WorkerState> _workers = new();

    private bool _shutdown;
    private bool _started;
    private int _nextWorkerId = 1;
    private Thread? _watchdogThread;
    private bool _watchdogStop;

    public DynamicThreadPool(DynamicThreadPoolOptions? options = null)
    {
        _options = options ?? new DynamicThreadPoolOptions();
        if (_options.MinThreads < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinThreads must be >= 1.");
        if (_options.MaxThreads < _options.MinThreads)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxThreads must be >= MinThreads.");
    }

    public int QueueDepth
    {
        get
        {
            lock (_sync) return _queue.Count;
        }
    }

    public int LiveWorkers
    {
        get
        {
            lock (_sync) return _workers.Count;
        }
    }

    /// <summary>События мониторинга (очередь, потоки, ошибки). Имя не «Monitor», чтобы не конфликтовать с <see cref="System.Threading.Monitor"/>.</summary>
    public event EventHandler<PoolMonitorEventArgs>? Monitoring;

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
                return;
            _started = true;
            _shutdown = false;
            for (int i = 0; i < _options.MinThreads; i++)
                SpawnWorkerLocked(reason: "initial-min");
            StartWatchdogLocked();
        }
    }

    /// <summary>Добавить задачу в очередь. Пул должен быть запущен (<see cref="Start"/>).</summary>
    public void Enqueue(Action work, string? name = null)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));

        lock (_sync)
        {
            if (!_started || _shutdown)
                throw new InvalidOperationException("Pool is not started or already shut down.");

            _queue.Enqueue(new WorkItem(work, name, DateTime.UtcNow));
            Raise(PoolMonitorKind.WorkEnqueued, message: name, queueDepth: _queue.Count);
            TryScaleUpLocked();
            SysMonitor.PulseAll(_sync);
        }
    }

    /// <summary>Остановка: новые задачи не принимаются, очередь дробится, потоки завершаются.</summary>
    public void Shutdown(bool waitForWorkers = true)
    {
        lock (_sync)
        {
            if (!_started || _shutdown)
                return;
            Raise(PoolMonitorKind.ShutdownStarted, message: "Shutdown requested");
            _shutdown = true;
            _watchdogStop = true;
            SysMonitor.PulseAll(_sync);
        }

        if (_watchdogThread != null && _watchdogThread.IsAlive)
            _watchdogThread.Join(TimeSpan.FromSeconds(5));

        if (waitForWorkers)
        {
            lock (_sync)
            {
                while (_workers.Count > 0)
                    SysMonitor.Wait(_sync);
            }
        }

        Raise(PoolMonitorKind.ShutdownCompleted, message: "Shutdown complete", liveWorkers: 0, queueDepth: 0);
    }

    public void Dispose() => Shutdown(waitForWorkers: true);

    private void StartWatchdogLocked()
    {
        _watchdogStop = false;
        _watchdogThread = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "DynamicThreadPool-Watchdog"
        };
        _watchdogThread.Start();
    }

    private void WatchdogLoop()
    {
        while (!_watchdogStop)
        {
            Thread.Sleep(_options.WatchdogPeriodMs);
            if (_watchdogStop)
                break;

            lock (_sync)
            {
                if (_shutdown)
                    break;

                var now = DateTime.UtcNow;
                foreach (var w in _workers)
                {
                    if (w.CurrentWorkStartedUtc is { } started)
                    {
                        var busyMs = (now - started).TotalMilliseconds;
                        if (busyMs >= _options.StuckWorkThresholdMs)
                        {
                            Raise(PoolMonitorKind.StuckWorkerSuspected,
                                message: $"Worker {w.Id} busy for {busyMs:F0} ms (threshold {_options.StuckWorkThresholdMs} ms)",
                                workerId: w.Id);
                            if (_workers.Count < _options.MaxThreads)
                                SpawnWorkerLocked(reason: "watchdog-replace-suspected-stuck");
                        }
                    }
                }
            }
        }
    }

    private void TryScaleUpLocked()
    {
        if (_shutdown)
            return;
        if (_workers.Count >= _options.MaxThreads)
            return;

        bool needMore = _queue.Count >= _options.ScaleUpQueueDepthThreshold;
        if (!needMore && _queue.Count > 0)
        {
            var oldest = PeekOldestEnqueueTime();
            if (oldest != null)
            {
                var waitMs = (DateTime.UtcNow - oldest.Value).TotalMilliseconds;
                if (waitMs >= _options.ScaleUpMaxQueueWaitMs)
                    needMore = true;
            }
        }

        if (needMore)
            SpawnWorkerLocked(reason: "scale-up");
    }

    private DateTime? PeekOldestEnqueueTime()
    {
        return _queue.Count > 0 ? _queue.Peek().EnqueuedUtc : null;
    }

    private void SpawnWorkerLocked(string reason)
    {
        if (_workers.Count >= _options.MaxThreads)
            return;

        int id = _nextWorkerId++;
        var state = new WorkerState { Id = id };
        _workers.Add(state);

        var thread = new Thread(() => WorkerLoop(state))
        {
            IsBackground = true,
            Name = $"PoolWorker-{id}"
        };
        thread.Start();
        Raise(PoolMonitorKind.WorkerSpawned, message: reason, workerId: id, liveWorkers: _workers.Count, queueDepth: _queue.Count);
    }

    private void WorkerLoop(WorkerState state)
    {
        try
        {
            while (true)
            {
                WorkItem? item = null;
                lock (_sync)
                {
                    while (_queue.Count == 0 && !_shutdown)
                    {
                        int waitMs = _workers.Count > _options.MinThreads
                            ? _options.WorkerIdleTimeoutMs
                            : System.Threading.Timeout.Infinite;

                        bool gotSignal = waitMs == System.Threading.Timeout.Infinite
                            ? SysMonitor.Wait(_sync)
                            : SysMonitor.Wait(_sync, waitMs);

                        if (!gotSignal && _queue.Count == 0 && !_shutdown)
                        {
                            if (_workers.Count > _options.MinThreads)
                            {
                                RemoveWorkerLocked(state);
                                Raise(PoolMonitorKind.WorkerExitedIdle,
                                    message: "Idle timeout shrink",
                                    workerId: state.Id,
                                    liveWorkers: _workers.Count,
                                    queueDepth: _queue.Count);
                                SysMonitor.PulseAll(_sync);
                                return;
                            }
                        }
                    }

                    if (_shutdown && _queue.Count == 0)
                    {
                        RemoveWorkerLocked(state);
                        SysMonitor.PulseAll(_sync);
                        return;
                    }

                    if (_queue.Count > 0)
                        item = _queue.Dequeue();
                }

                if (item == null)
                    continue;

                state.CurrentWorkStartedUtc = DateTime.UtcNow;
                Raise(PoolMonitorKind.WorkStarted, message: item.Name, workerId: state.Id, queueDepth: QueueDepth);

                try
                {
                    item.Work();
                    Raise(PoolMonitorKind.WorkCompleted, message: item.Name, workerId: state.Id);
                }
                catch (Exception ex)
                {
                    // Log and notify; do not immediately replace the worker here — replacement on thread crash or watchdog.
                    Raise(PoolMonitorKind.WorkFailed, message: item.Name, workerId: state.Id, error: ex);
                    LogError(ex);
                }
                finally
                {
                    state.CurrentWorkStartedUtc = null;
                }
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            lock (_sync)
            {
                RemoveWorkerLocked(state);
                Raise(PoolMonitorKind.WorkerCrashed, message: ex.Message, workerId: state.Id, error: ex, liveWorkers: _workers.Count);
                TryRecoverAfterWorkerFailure(state);
                SysMonitor.PulseAll(_sync);
            }
        }
    }

    private void RemoveWorkerLocked(WorkerState state)
    {
        _workers.Remove(state);
    }

    /// <summary>
    /// Отказоустойчивость: после сбоя рабочего потока создаём новый, если пул ещё активен и не превышен MaxThreads.
    /// </summary>
    private void TryRecoverAfterWorkerFailure(WorkerState failedState)
    {
        lock (_sync)
        {
            if (_shutdown)
                return;
            if (_workers.Count >= _options.MaxThreads)
                return;
            SpawnWorkerLocked(reason: "recovery-after-failure");
            Raise(PoolMonitorKind.WorkerReplaced, message: $"Replacement after worker {failedState.Id}", liveWorkers: _workers.Count);
        }
    }

    private void Raise(
        PoolMonitorKind kind,
        string? message = null,
        int? workerId = null,
        int? liveWorkers = null,
        int? queueDepth = null,
        Exception? error = null)
    {
        int lw = liveWorkers ?? LiveWorkers;
        int qd = queueDepth ?? QueueDepth;
        Monitoring?.Invoke(this, new PoolMonitorEventArgs
        {
            Kind = kind,
            UtcTime = DateTime.UtcNow,
            LiveWorkers = lw,
            QueueDepth = qd,
            WorkerId = workerId,
            Message = message,
            Error = error
        });
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O} [DynamicThreadPool] {ex}";
            Console.Error.WriteLine(line);
            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "threadpool-errors.log"),
                line + Environment.NewLine);
        }
        catch
        {
            // ignore logging failures
        }
    }

    private sealed class WorkItem
    {
        public WorkItem(Action work, string? name, DateTime enqueuedUtc)
        {
            Work = work;
            Name = name;
            EnqueuedUtc = enqueuedUtc;
        }

        public Action Work { get; }
        public string? Name { get; }
        public DateTime EnqueuedUtc { get; }
    }

    private sealed class WorkerState
    {
        public int Id { get; init; }
        public DateTime? CurrentWorkStartedUtc { get; set; }
    }
}
