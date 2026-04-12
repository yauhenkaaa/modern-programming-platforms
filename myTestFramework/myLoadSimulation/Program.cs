using System.Diagnostics;
using myTestedProject;
using myTestingLibrary;
using myThreadPool;

namespace myLoadSimulation;

/// <summary>
/// Моделирование неравномерной нагрузки на собственный пул потоков и запуск тестов (не менее 50 выполнений).
/// </summary>
internal static class Program
{
    private const int MinimumTestRuns = 1500;
    private static readonly object s_resultsLock = new();
    private static readonly List<TestRunnerCore.TestResult> s_poolResults = new();
    private static int s_enqueued;
    private static int s_completed;

    // Lock to serialize tests that rely on shared mutable context (SharedContextAttribute).
    private static readonly object s_sharedContextLock = new();

    private static void Main()
    {
        string? testDll = TestRunnerCore.ResolveTestAssemblyPath();
        if (testDll == null || !File.Exists(testDll))
        {
            Console.WriteLine("Error: myProjectTests.dll not found. Build myProjectTests (Debug) first.");
            Console.WriteLine("Expected path pattern: .../myTestFramework/myProjectTests/bin/Debug/net8.0/myProjectTests.dll");
            return;
        }

        string testDir = Path.GetDirectoryName(testDll)!;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => TestRunnerCore.ResolveAssemblyFromDirectory(testDir, s, e);

        Console.WriteLine("Test assembly: " + testDll);
        var structure = TestRunnerCore.DiscoverTests(testDll);
        if (structure.Count == 0)
        {
            Console.WriteLine("No tests found.");
            return;
        }

        var inventory = new InventoryService();
        TestContextContainer.RegisterSharedObject(inventory);

        var workItems = TestRunnerCore.BuildWorkItems(structure);
        if (workItems.Count == 0)
        {
            Console.WriteLine("No test methods.");
            return;
        }

        Console.WriteLine($"Discovered {workItems.Count} test method(s). Target total runs: >= {MinimumTestRuns}.");
        Console.WriteLine();

        var poolOptions = new DynamicThreadPoolOptions
        {
            MinThreads = 4,
            MaxThreads = 12,
            WorkerIdleTimeoutMs = 2500,
            ScaleUpQueueDepthThreshold = 6,
            ScaleUpMaxQueueWaitMs = 400,
            WatchdogPeriodMs = 500,
            StuckWorkThresholdMs = 120_000
        };

        using var pool = new DynamicThreadPool(poolOptions);
        pool.Monitoring += OnPoolMonitoring;

        pool.Start();
        Console.WriteLine($"Pool started: Min={poolOptions.MinThreads}, Max={poolOptions.MaxThreads}");
        Console.WriteLine("--- Load scenario: idle, bursts, singles ---");

        var swPool = Stopwatch.StartNew();
        RunLoadScenario(pool, workItems);
        swPool.Stop();

        pool.Shutdown(waitForWorkers: true);

        List<TestRunnerCore.TestResult> poolSnapshot;
        lock (s_resultsLock)
        {
            poolSnapshot = s_poolResults.ToList();
        }

        Console.WriteLine();
        Console.WriteLine($"=== Pool run finished: {poolSnapshot.Count} test executions in {swPool.ElapsedMilliseconds} ms ===");
        PrintSummary(poolSnapshot);

        // Демонстрация: тот же набор запусков в одном потоке (без пула)
        Console.WriteLine();
        Console.WriteLine("--- Sequential comparison (same count, single-threaded) ---");
        var sequential = new List<TestRunnerCore.TestResult>();
        var swSeq = Stopwatch.StartNew();
        foreach (var r in Enumerable.Range(0, poolSnapshot.Count))
        {
            var wi = workItems[r % workItems.Count];
            sequential.Add(TestRunnerCore.ExecuteWorkItem(wi));
        }
        swSeq.Stop();
        Console.WriteLine($"Sequential: {sequential.Count} runs in {swSeq.ElapsedMilliseconds} ms.");
        double ratio = swPool.Elapsed.TotalSeconds / Math.Max(swSeq.Elapsed.TotalSeconds, 1e-6);
        Console.WriteLine($"Wall-clock ratio (pool / sequential): {ratio:F2} (при очень коротких тестах пул может быть медленнее из‑за накладных расходов и синхронизации)");
    }

    private static void OnPoolMonitoring(object? sender, PoolMonitorEventArgs e)
    {
        string msg = e.Message ?? e.Kind.ToString();
        Console.WriteLine($"[{e.UtcTime:HH:mm:ss.fff}] {e.Kind} | workers={e.LiveWorkers} queue={e.QueueDepth} | {msg}");
    }

    /// <summary>
    /// Неравномерная подача: паузы, пики, одиночные задачи; всего не менее <see cref="MinimumTestRuns"/> запусков.
    /// </summary>
    private static void RunLoadScenario(DynamicThreadPool pool, List<TestRunnerCore.TestWorkItem> workItems)
    {
        s_poolResults.Clear();
        Interlocked.Exchange(ref s_enqueued, 0);
        Interlocked.Exchange(ref s_completed, 0);
        int totalRuns = 0;
        int idx = 0;

        void EnqueueOne()
        {
            var wi = workItems[idx % workItems.Count];
            idx++;
            Interlocked.Increment(ref s_enqueued);
            pool.Enqueue(() =>
            {
                try
                {
                    if (wi.IsExclusive)
                    {
                        lock (s_sharedContextLock)
                        {
                            var r = TestRunnerCore.ExecuteWorkItem(wi);
                            lock (s_resultsLock) s_poolResults.Add(r);
                        }
                    }
                    else
                    {
                        var r = TestRunnerCore.ExecuteWorkItem(wi);
                        lock (s_resultsLock) s_poolResults.Add(r);
                    }
                }
                finally
                {
                    Interlocked.Increment(ref s_completed);
                }
            }, wi.FullName);
            totalRuns++;
        }

        // Фаза: простой
        Console.WriteLine("[Phase] Idle 800 ms");
        // Thread.Sleep(800);

        // Пик: много задач сразу
        Console.WriteLine("[Phase] Burst 18 tasks");
        for (int i = 0; i < 18; i++)
            EnqueueOne();

        // Thread.Sleep(300);

        // Одиночные с интервалом
        Console.WriteLine("[Phase] Single every 120 ms (x12)");
        for (int i = 0; i < 12; i++)
        {
            EnqueueOne();
            // Thread.Sleep(120);
        }

        // Ещё простой
        Console.WriteLine("[Phase] Idle 500 ms");
        // Thread.Sleep(500);

        // Второй пик
        Console.WriteLine("[Phase] Burst 15 tasks");
        for (int i = 0; i < 15; i++)
            EnqueueOne();

        // Thread.Sleep(200);

        // Добиваем до минимума 50+ запусков
        Console.WriteLine("[Phase] Fill to minimum runs");
        while (totalRuns < MinimumTestRuns)
            EnqueueOne();

        // Небольшой хвост
        int extra = 5;
        while (extra-- > 0)
            EnqueueOne();

        // Пока задачи в работе, очередь может быть пуста — ждём счётчик завершений.
        int spins = 0;
        while (Volatile.Read(ref s_completed) < Volatile.Read(ref s_enqueued) && spins < 20_000)
        {
            Thread.Sleep(1);
            spins++;
        }
    }

    private static void PrintSummary(List<TestRunnerCore.TestResult> results)
    {
        int total = results.Count;
        int passed = results.Count(r => r.Passed);
        int failed = total - passed;
        double pct = total == 0 ? 0 : 100.0 * passed / total;
        Console.WriteLine($"Total: {total}, Passed: {passed}, Failed: {failed}, Success: {pct:F1}%");
    }
}
