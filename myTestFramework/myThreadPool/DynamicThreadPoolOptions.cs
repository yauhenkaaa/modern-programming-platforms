namespace myThreadPool;

/// <summary>
/// Параметры динамического пула потоков (границы, таймауты простоя, пороги масштабирования).
/// </summary>
public sealed class DynamicThreadPoolOptions
{
    /// <summary>Минимальное число рабочих потоков (не опускается ниже при сжатии).</summary>
    public int MinThreads { get; init; } = 2;

    /// <summary>Максимальное число рабочих потоков.</summary>
    public int MaxThreads { get; init; } = 8;

    /// <summary>Время простоя в очереди ожидания работы, после которого поток может завершиться (если число потоков &gt; MinThreads).</summary>
    public int WorkerIdleTimeoutMs { get; init; } = 3000;

    /// <summary>Если длина очереди достигла или превысила это значение — пробуем добавить поток (если &lt; MaxThreads).</summary>
    public int ScaleUpQueueDepthThreshold { get; init; } = 3;

    /// <summary>Если самая старая задача в очереди ждёт дольше (мс) — пробуем добавить поток.</summary>
    public int ScaleUpMaxQueueWaitMs { get; init; } = 500;

    /// <summary>Период опроса для мониторинга «зависших» потоков (доп. задание).</summary>
    public int WatchdogPeriodMs { get; init; } = 1000;

    /// <summary>Считать поток «зависшим», если он выполняет одну задачу дольше этого времени (мс).</summary>
    public int StuckWorkThresholdMs { get; init; } = 120_000;
}
