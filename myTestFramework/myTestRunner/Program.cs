using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;
using myThreadPool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace myTestRunner
{
    /// <summary>
    /// Test execution loop, async handling, result collection, and thread pool integration.
    /// </summary>
    public class Program
    {
        /// <summary>Directory of the loaded test assembly; used to resolve dependencies.</summary>
        private static string? s_testAssemblyDirectory;

        /// <summary>Lock object for synchronized console output from parallel tests.</summary>
        private static readonly object s_consoleLock = new object();

        /// <summary>
        /// Делегат для фильтрации тестов на этапе обнаружения.
        /// </summary>
        public delegate bool TestFilter(Type testClass, MethodInfo testMethod, TestMethodAttribute methodAttr);

        private sealed class TestResult
        {
            public string FullName { get; set; } = "";
            public bool Passed { get; set; }
            public string? ErrorMessage { get; set; }
        }

        private sealed class TestWorkItem
        {
            public Type TestClassType { get; init; } = null!;
            public MethodInfo Method { get; init; } = null!;
            public string FullName { get; init; } = "";
            public int TimeoutMilliseconds { get; init; }
            public object[]? Arguments { get; init; } // Добавлено для параметризованных тестов
        }

        static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configuration = "Debug";

            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            while (dir != null && !dir.Name.Equals("myTestFramework", StringComparison.OrdinalIgnoreCase))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                Console.WriteLine("Error: Unable to locate 'myTestFramework' directory from: " + baseDir);
                return;
            }

            string testDllPath = Path.Combine(
                dir.FullName,
                "myProjectTests",
                "bin",
                configuration,
                "net8.0",
                "myProjectTests.dll");

            var fileInfo = new FileInfo(testDllPath);
            if (!fileInfo.Exists)
            {
                Console.WriteLine("Error: Test file not found: " + testDllPath);
                return;
            }

            string fullPath = Path.GetFullPath(fileInfo.FullName);
            s_testAssemblyDirectory = Path.GetDirectoryName(fullPath);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromTestDirectory;

            Console.WriteLine("Test file found: " + fullPath);
            Console.WriteLine();

            // Применяем фильтрацию через делегат. 
            // В данном примере пропускаем тесты, у которых стоит флаг Ignore = true.
            TestFilter myFilter = (testClass, testMethod, methodAttr) => !methodAttr.Ignore;

            var workItems = DiscoverAndFilterTests(fullPath, myFilter);
            if (workItems.Count == 0)
            {
                Console.WriteLine("No test classes or methods found matching the filter.");
                return;
            }

            // Register shared context
            var sharedInventory = new InventoryService();
            TestContextContainer.RegisterSharedObject(sharedInventory);

            // Инициализация собственного пула потоков
            int maxDegreeOfParallelism = Environment.ProcessorCount;
            if (args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0)
            {
                maxDegreeOfParallelism = parsed;
            }

            var poolOptions = new DynamicThreadPoolOptions
            {
                MinThreads = 2,
                MaxThreads = maxDegreeOfParallelism,
                WorkerIdleTimeoutMs = 3000
            };

            using var pool = new DynamicThreadPool(poolOptions);

            // Подписка на события жизненного цикла пула потоков
            pool.Monitoring += OnPoolMonitoring;
            pool.Start();

            Console.WriteLine($"Pool started with MaxThreads = {poolOptions.MaxThreads}. Enqueuing {workItems.Count} tests...");

            var resultsBag = new ConcurrentBag<TestResult>();
            using var countdownEvent = new CountdownEvent(workItems.Count);
            DateTime startTime = DateTime.Now;

            foreach (var workItem in workItems)
            {
                pool.Enqueue(() =>
                {
                    try
                    {
                        var result = ExecuteTestItem(workItem);
                        resultsBag.Add(result);
                        PrintTestResult(result);
                    }
                    finally
                    {
                        countdownEvent.Signal();
                    }
                }, workItem.FullName);
            }

            // Ожидаем завершения всех задач в пуле
            countdownEvent.Wait();
            DateTime endTime = DateTime.Now;

            pool.Shutdown(waitForWorkers: true);

            var orderedResults = resultsBag.OrderBy(r => r.FullName).ToList();
            PrintSummary(orderedResults);
            WriteTestProtocol(orderedResults, startTime, endTime);
        }

        private static Assembly? ResolveAssemblyFromTestDirectory(object? sender, ResolveEventArgs args)
        {
            if (string.IsNullOrEmpty(s_testAssemblyDirectory))
                return null;
            string simpleName = new AssemblyName(args.Name).Name ?? "";
            if (string.IsNullOrEmpty(simpleName))
                return null;
            string path = Path.Combine(s_testAssemblyDirectory, simpleName + ".dll");
            if (!File.Exists(path))
                return null;
            return Assembly.LoadFrom(path);
        }

        private static List<TestWorkItem> DiscoverAndFilterTests(string assemblyPath, TestFilter filter)
        {
            var workItems = new List<TestWorkItem>();
            var assembly = Assembly.LoadFrom(assemblyPath);
            Type[] types = assembly.GetTypes();

            foreach (Type type in types)
            {
                var classAttr = type.GetCustomAttribute<TestClassAttribute>();
                if (classAttr == null)
                    continue;

                int classTimeout = classAttr.Timeout > 0 ? classAttr.Timeout : 0;

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                foreach (MethodInfo method in methods)
                {
                    var methodAttr = method.GetCustomAttribute<TestMethodAttribute>();
                    if (methodAttr == null)
                        continue;

                    // Применяем фильтр
                    if (!filter(type, method, methodAttr))
                        continue;

                    int timeout = methodAttr.Timeout > 0 ? methodAttr.Timeout : classTimeout;
                    string baseFullName = $"{type.FullName}.{method.Name}";

                    // Проверяем наличие атрибута параметризованного теста
                    var dynamicDataAttr = method.GetCustomAttribute<DynamicDataAttribute>();
                    if (dynamicDataAttr != null)
                    {
                        var dataMethod = type.GetMethod(dynamicDataAttr.MethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (dataMethod != null)
                        {
                            var testData = (IEnumerable<object[]>)dataMethod.Invoke(null, null)!;
                            int caseIndex = 1;
                            foreach (var args in testData)
                            {
                                workItems.Add(new TestWorkItem
                                {
                                    TestClassType = type,
                                    Method = method,
                                    FullName = $"{baseFullName} [Case {caseIndex++}]",
                                    TimeoutMilliseconds = timeout,
                                    Arguments = args
                                });
                            }
                            continue;
                        }
                    }

                    // Обычный тест (без параметров)
                    workItems.Add(new TestWorkItem
                    {
                        TestClassType = type,
                        Method = method,
                        FullName = baseFullName,
                        TimeoutMilliseconds = timeout,
                        Arguments = null
                    });
                }
            }

            return workItems;
        }

        private static void OnPoolMonitoring(object? sender, PoolMonitorEventArgs e)
        {
            // Фильтруем события, чтобы не засорять консоль каждым стартом/завершением задачи
            if (e.Kind == PoolMonitorKind.WorkerSpawned ||
                e.Kind == PoolMonitorKind.WorkerCrashed ||
                e.Kind == PoolMonitorKind.ShutdownCompleted ||
                e.Kind == PoolMonitorKind.StuckWorkerSuspected)
            {
                lock (s_consoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[POOL] {e.UtcTime:HH:mm:ss.fff} | {e.Kind} | Workers: {e.LiveWorkers} | {e.Message}");
                    Console.ResetColor();
                }
            }
        }

        private static TestResult ExecuteTestItem(TestWorkItem workItem)
        {
            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(workItem.TestClassType);
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    FullName = workItem.FullName,
                    Passed = false,
                    ErrorMessage = "Failed to create test class instance: " + ex.Message
                };
            }

            InjectSharedContext(instance!, workItem.TestClassType);

            return RunSingleTestWithTimeout(instance!, workItem.TestClassType, workItem.Method, workItem.FullName, workItem.TimeoutMilliseconds, workItem.Arguments);
        }

        private static void InjectSharedContext(object instance, Type testClassType)
        {
            var sharedContextAttr = typeof(SharedContextAttribute);
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (FieldInfo field in testClassType.GetFields(bindingFlags))
            {
                if (field.GetCustomAttribute(sharedContextAttr) == null)
                    continue;
                Type fieldType = field.FieldType;
                object? value = GetFromContainer(fieldType);
                field.SetValue(instance, value);
            }

            foreach (PropertyInfo prop in testClassType.GetProperties(bindingFlags))
            {
                if (prop.GetCustomAttribute(sharedContextAttr) == null)
                    continue;
                if (!prop.CanWrite)
                    continue;
                Type propType = prop.PropertyType;
                object? value = GetFromContainer(propType);
                prop.SetValue(instance, value, null);
            }
        }

        private static object? GetFromContainer(Type type)
        {
            var getMethod = typeof(TestContextContainer)
                .GetMethods()
                .First(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            return getMethod.MakeGenericMethod(type).Invoke(null, null);
        }

        private static void InvokeLifecycleMethods(object instance, Type testClassType, Type attributeType)
        {
            var methods = testClassType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute(attributeType) != null);
            foreach (var m in methods)
            {
                object? ret = m.Invoke(instance, null);
                if (ret is Task t)
                    t.GetAwaiter().GetResult();
            }
        }

        private static TestResult RunSingleTest(object instance, Type testClassType, MethodInfo method, string fullName, object[]? arguments)
        {
            try
            {
                InvokeLifecycleMethods(instance, testClassType, typeof(BeforeEachAttribute));
                try
                {
                    object? returnValue = method.Invoke(instance, arguments);

                    if (returnValue is Task task)
                        task.GetAwaiter().GetResult();

                    return new TestResult { FullName = fullName, Passed = true };
                }
                finally
                {
                    InvokeLifecycleMethods(instance, testClassType, typeof(AfterEachAttribute));
                }
            }
            catch (TargetInvocationException tie)
            {
                Exception ex = tie.InnerException ?? tie;
                string message = ex.Message;
                if (ex.StackTrace != null)
                    message += Environment.NewLine + ex.StackTrace;
                return new TestResult { FullName = fullName, Passed = false, ErrorMessage = message };
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                if (ex.StackTrace != null)
                    message += Environment.NewLine + ex.StackTrace;
                return new TestResult { FullName = fullName, Passed = false, ErrorMessage = message };
            }
        }

        private static TestResult RunSingleTestWithTimeout(object instance, Type testClassType, MethodInfo method, string fullName, int timeoutMs, object[]? arguments)
        {
            if (timeoutMs <= 0)
            {
                return RunSingleTest(instance, testClassType, method, fullName, arguments);
            }

            try
            {
                var task = Task.Run(() => RunSingleTest(instance, testClassType, method, fullName, arguments));
                bool completedInTime = task.Wait(timeoutMs);
                if (!completedInTime)
                {
                    return new TestResult
                    {
                        FullName = fullName,
                        Passed = false,
                        ErrorMessage = $"Test timed out after {timeoutMs} ms."
                    };
                }

                return task.Result;
            }
            catch (AggregateException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                string message = inner.Message;
                if (inner.StackTrace != null)
                    message += Environment.NewLine + inner.StackTrace;
                return new TestResult { FullName = fullName, Passed = false, ErrorMessage = message };
            }
        }

        private static void PrintTestResult(TestResult result)
        {
            lock (s_consoleLock)
            {
                if (result.Passed)
                {
                    Console.BackgroundColor = ConsoleColor.Green;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.Write(" SUCCESS ");
                    Console.ResetColor();
                    Console.WriteLine($" {result.FullName}");
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.Red;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(" FAILURE ");
                    Console.ResetColor();
                    Console.WriteLine($" {result.FullName}");
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.WriteLine(result.ErrorMessage);
                        Console.ResetColor();
                    }
                }
                Console.WriteLine();
            }
        }

        private static void PrintSummary(List<TestResult> results)
        {
            int total = results.Count;
            int passed = results.Count(r => r.Passed);
            int failed = total - passed;

            double successPercent = total == 0 ? 0.0 : (double)passed / total * 100.0;

            Console.WriteLine("========== Test run summary ==========");
            Console.WriteLine($"Total tests:  {total}");
            Console.WriteLine($"Passed:       {passed}");
            Console.WriteLine($"Failed:       {failed}");
            Console.WriteLine($"Success rate: {successPercent:F2}%");
            Console.WriteLine();
            Console.WriteLine("Tests:");
            foreach (var r in results)
            {
                string status = r.Passed ? " [OK] [v]" : " [FAIL] [x]";
                Console.WriteLine($"  {r.FullName}{status}");
            }
            Console.WriteLine("======================================");
        }

        private static void WriteTestProtocol(List<TestResult> results, DateTime startTime, DateTime endTime)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                DirectoryInfo? dir = new DirectoryInfo(baseDir);
                while (dir != null && !dir.Name.Equals("myTestRunner", StringComparison.OrdinalIgnoreCase))
                {
                    dir = dir.Parent;
                }

                if (dir == null)
                    return;

                string resultsDir = Path.Combine(dir.FullName, "TestResults");
                Directory.CreateDirectory(resultsDir);

                string fileName = $"TestResults_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(resultsDir, fileName);

                int total = results.Count;
                int passed = results.Count(r => r.Passed);
                int failed = total - passed;
                double successPercent = total == 0 ? 0.0 : (double)passed / total * 100.0;
                TimeSpan duration = endTime - startTime;

                using (var writer = new StreamWriter(filePath, false))
                {
                    writer.WriteLine("========== Test protocol ==========");
                    writer.WriteLine($"Start time:           {startTime:O}");
                    writer.WriteLine($"End time:             {endTime:O}");
                    writer.WriteLine($"Total duration:       {duration}");
                    writer.WriteLine();
                    writer.WriteLine($"Total tests:          {total}");
                    writer.WriteLine($"Successful tests:     {passed}");
                    writer.WriteLine($"Failed tests:         {failed}");
                    writer.WriteLine($"Success percentage:   {successPercent:F2}%");
                    writer.WriteLine();

                    if (failed > 0)
                    {
                        writer.WriteLine("Failed tests detail:");
                        foreach (var r in results.Where(r => !r.Passed))
                        {
                            writer.WriteLine("--------------------------------------");
                            writer.WriteLine($"Test: {r.FullName}");
                            if (!string.IsNullOrEmpty(r.ErrorMessage))
                            {
                                writer.WriteLine("Error:");
                                writer.WriteLine(r.ErrorMessage);
                            }
                        }
                    }

                    writer.WriteLine("====================================");
                }
            }
            catch
            {
                // Ignore any IO/permissions errors when writing protocol.
            }
        }
    }
}