using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace myTestRunner
{
    /// <summary>
    /// Test execution loop, async handling, and result collection.
    /// </summary>
    public class Program
    {
        /// <summary>Directory of the loaded test assembly; used to resolve dependencies (e.g. myTestedProject).</summary>
        private static string? s_testAssemblyDirectory;

        /// <summary>Lock object for synchronized console output from parallel tests.</summary>
        private static readonly object s_consoleLock = new object();

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
        }

        static void Main(string[] args)
        {
            // Always use myProjectTests.dll artifact as the test assembly.
            // Assume runner is executed from myTestFramework/myTestRunner/bin/<Configuration>/net8.0
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // baseDir: .../myTestFramework/myTestRunner/bin/<Configuration>/net8.0/

            // В задании ваш тестовый проект собирается в Debug/net8.0,
            // поэтому жёстко используем конфигурацию Debug.
            string configuration = "Debug";

            // Find solution root folder "myTestFramework" by walking up the directory tree
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

            // dir is .../myTestFramework
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

            // Configure MaxDegreeOfParallelism:
            //  - first argument (if present) tries to override it
            //  - otherwise use number of logical processors
            int maxDegreeOfParallelism = Environment.ProcessorCount;
            if (args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0)
            {
                maxDegreeOfParallelism = parsed;
            }

            var testStructure = DiscoverTests(fullPath);
            if (testStructure.Count == 0)
            {
                Console.WriteLine("No test classes or methods found in the assembly.");
                return;
            }

            // Register shared context: one InventoryService instance injected into test classes via [SharedContext].
            var sharedInventory = new InventoryService();
            TestContextContainer.RegisterSharedObject(sharedInventory);

            DateTime startTime = DateTime.Now;
            var results = RunAllTests(testStructure, maxDegreeOfParallelism);
            DateTime endTime = DateTime.Now;

            PrintSummary(results);
            WriteTestProtocol(results, startTime, endTime);
        }

        /// <summary>
        /// Resolves assemblies (e.g. myTestedProject, myTestingLibrary) from the same directory as the test assembly.
        /// </summary>
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

        private static Dictionary<Type, List<MethodInfo>> DiscoverTests(string assemblyPath)
        {
            var testStructure = new Dictionary<Type, List<MethodInfo>>();
            var assembly = Assembly.LoadFrom(assemblyPath);
            Type[] types = assembly.GetTypes();

            foreach (Type type in types)
            {
                if (type.GetCustomAttribute(typeof(TestClassAttribute)) == null)
                    continue;

                var testMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.GetCustomAttribute(typeof(TestMethodAttribute)) != null)
                    .ToList();

                if (testMethods.Count > 0)
                    testStructure.Add(type, testMethods);
            }

            return testStructure;
        }

        private static List<TestResult> RunAllTests(Dictionary<Type, List<MethodInfo>> testStructure, int maxDegreeOfParallelism)
        {
            var workItems = new List<TestWorkItem>();

            foreach (var kv in testStructure.OrderBy(k => k.Key.Name))
            {
                Type testClassType = kv.Key;
                List<MethodInfo> methods = kv.Value;

                int classTimeout = 0;
                var classAttr = testClassType.GetCustomAttribute<TestClassAttribute>();
                if (classAttr != null && classAttr.Timeout > 0)
                {
                    classTimeout = classAttr.Timeout;
                }

                foreach (MethodInfo method in methods)
                {
                    string fullName = $"{testClassType.FullName}.{method.Name}";

                    int timeout = classTimeout;
                    var methodTimeoutAttr = method.GetCustomAttribute<TestClassAttribute>();
                    if (methodTimeoutAttr != null && methodTimeoutAttr.Timeout > 0)
                    {
                        timeout = methodTimeoutAttr.Timeout;
                    }

                    workItems.Add(new TestWorkItem
                    {
                        TestClassType = testClassType,
                        Method = method,
                        FullName = fullName,
                        TimeoutMilliseconds = timeout
                    });
                }
            }

            var resultsBag = new ConcurrentBag<TestResult>();

            Parallel.ForEach(
                workItems,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                workItem =>
                {
                    object? instance = null;
                    try
                    {
                        instance = Activator.CreateInstance(workItem.TestClassType);
                    }
                    catch (Exception ex)
                    {
                        var failResult = new TestResult
                        {
                            FullName = workItem.FullName,
                            Passed = false,
                            ErrorMessage = "Failed to create test class instance: " + ex.Message
                        };
                        resultsBag.Add(failResult);
                        return;
                    }

                    InjectSharedContext(instance!, workItem.TestClassType);

                    var result = RunSingleTestWithTimeout(instance!, workItem.TestClassType, workItem.Method, workItem.FullName, workItem.TimeoutMilliseconds);
                    resultsBag.Add(result);
                });
            
            var orderedResults = resultsBag.OrderBy(r => r.FullName).ToList();
            foreach (var result in orderedResults)
            {
                PrintTestResult(result);
            }

            return orderedResults;
        }

        /// <summary>
        /// Finds all fields and properties marked with [SharedContext], retrieves the corresponding
        /// instances from TestContextContainer by type, and sets them on the test class instance.
        /// </summary>
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

        private static TestResult RunSingleTest(object instance, Type testClassType, MethodInfo method, string fullName)
        {
            try
            {
                InvokeLifecycleMethods(instance, testClassType, typeof(BeforeEachAttribute));
                try
                {
                    object? returnValue = method.Invoke(instance, null);

                    if (returnValue is Task task)
                        task.GetAwaiter().GetResult();

                    return new TestResult { FullName = fullName, Passed = true };
                }
                finally
                {
                    InvokeLifecycleMethods(instance, testClassType, typeof(AfterEachAttribute));
                }
            }
            catch (Exception ex)
            {
                string message = ex.InnerException?.Message ?? ex.Message;
                if (ex.InnerException?.StackTrace != null)
                    message += Environment.NewLine + ex.InnerException.StackTrace;
                return new TestResult { FullName = fullName, Passed = false, ErrorMessage = message };
            }
        }

        /// <summary>
        /// Wraps RunSingleTest with optional timeout, using TestClassAttribute.Timeout
        /// from the test class or method. If timeoutMs &lt;= 0, test runs without timeout.
        /// </summary>
        private static TestResult RunSingleTestWithTimeout(object instance, Type testClassType, MethodInfo method, string fullName, int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                return RunSingleTest(instance, testClassType, method, fullName);
            }

            try
            {
                var task = Task.Run(() => RunSingleTest(instance, testClassType, method, fullName));
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
                string message = ex.InnerException?.Message ?? ex.Message;
                if (ex.InnerException?.StackTrace != null)
                    message += Environment.NewLine + ex.InnerException.StackTrace;
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

        /// <summary>
        /// Writes a textual test protocol into MyTestRunner/TestResults.
        /// Contains start time, duration, successful tests count, success percentage,
        /// and error information for failed tests.
        /// </summary>
        private static void WriteTestProtocol(List<TestResult> results, DateTime startTime, DateTime endTime)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Locate 'myTestFramework/myTestRunner' project directory to place TestResults there.
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
