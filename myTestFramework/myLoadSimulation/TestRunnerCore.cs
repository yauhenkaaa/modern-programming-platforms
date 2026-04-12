using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;
using System.Reflection;

namespace myLoadSimulation;

/// <summary>
/// Загрузка сборки тестов, обнаружение методов и выполнение одного теста.
/// Таймаут реализован через отдельный поток и <see cref="Thread.Join(int)"/> (без Task.Run).
/// </summary>
public static class TestRunnerCore
{
    public sealed class TestResult
    {
        public string FullName { get; set; } = "";
        public bool Passed { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class TestWorkItem
    {
        public Type TestClassType { get; init; } = null!;
        public MethodInfo Method { get; init; } = null!;
        public string FullName { get; init; } = "";
        public int TimeoutMilliseconds { get; init; }
        public bool IsExclusive { get; init; } = false; // new: требование эксклюзивного доступа к shared context
    }

    public static string? ResolveTestAssemblyPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string configuration = "Debug";
        DirectoryInfo? dir = new DirectoryInfo(baseDir);
        while (dir != null && !dir.Name.Equals("myTestFramework", StringComparison.OrdinalIgnoreCase))
            dir = dir.Parent;
        if (dir == null)
            return null;
        return Path.Combine(dir.FullName, "myProjectTests", "bin", configuration, "net8.0", "myProjectTests.dll");
    }

    public static Assembly? ResolveAssemblyFromDirectory(string? testDir, object? sender, ResolveEventArgs args)
    {
        if (string.IsNullOrEmpty(testDir))
            return null;
        string simpleName = new AssemblyName(args.Name).Name ?? "";
        if (string.IsNullOrEmpty(simpleName))
            return null;
        string path = Path.Combine(testDir, simpleName + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    public static Dictionary<Type, List<MethodInfo>> DiscoverTests(string assemblyPath)
    {
        var testStructure = new Dictionary<Type, List<MethodInfo>>();
        var assembly = Assembly.LoadFrom(assemblyPath);
        foreach (Type type in assembly.GetTypes())
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

    public static List<TestWorkItem> BuildWorkItems(Dictionary<Type, List<MethodInfo>> testStructure)
    {
        var list = new List<TestWorkItem>();
        var sharedAttr = typeof(SharedContextAttribute);
        var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var kv in testStructure.OrderBy(k => k.Key.Name))
        {
            // detect if this test class actually uses [SharedContext] on any field/property
            bool usesSharedContext = kv.Key.GetFields(bindingFlags).Any(f => f.GetCustomAttribute(sharedAttr) != null)
                                     || kv.Key.GetProperties(bindingFlags).Any(p => p.GetCustomAttribute(sharedAttr) != null);

            foreach (MethodInfo method in kv.Value)
            {
                var tm = method.GetCustomAttribute<TestMethodAttribute>();
                int timeout = tm != null && tm.Timeout > 0 ? tm.Timeout : 0;
                list.Add(new TestWorkItem
                {
                    TestClassType = kv.Key,
                    Method = method,
                    FullName = $"{kv.Key.FullName}.{method.Name}",
                    TimeoutMilliseconds = timeout,
                    IsExclusive = usesSharedContext
                });
            }
        }
        return list;
    }

    public static void InjectSharedContext(object instance, Type testClassType)
    {
        var sharedContextAttr = typeof(SharedContextAttribute);
        var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (FieldInfo field in testClassType.GetFields(bindingFlags))
        {
            if (field.GetCustomAttribute(sharedContextAttr) == null)
                continue;
            object? value = GetFromContainer(field.FieldType);
            field.SetValue(instance, value);
        }
        foreach (PropertyInfo prop in testClassType.GetProperties(bindingFlags))
        {
            if (prop.GetCustomAttribute(sharedContextAttr) == null || !prop.CanWrite)
                continue;
            object? value = GetFromContainer(prop.PropertyType);
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

    public static TestResult RunSingleTest(object instance, Type testClassType, MethodInfo method, string fullName)
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

    /// <summary>Таймаут реализован путем ожидания возвращаемого Task (если он есть) с таймаутом. Не создаются дополнительные фоновые потоки.</summary>
    public static TestResult RunSingleTestWithTimeout(object instance, Type testClassType, MethodInfo method, string fullName, int timeoutMs)
    {
        if (timeoutMs <= 0)
            return RunSingleTest(instance, testClassType, method, fullName);

        try
        {
            // Запускаем жизненный цикл и сам тест, но если метод возвращает Task, ждём его с таймаутом.
            InvokeLifecycleMethods(instance, testClassType, typeof(BeforeEachAttribute));
            try
            {
                object? returnValue = null;
                try
                {
                    returnValue = method.Invoke(instance, null);
                }
                catch (TargetInvocationException tie)
                {
                    // рефлексия обернула исключение
                    throw tie.InnerException ?? tie;
                }

                if (returnValue is Task task)
                {
                    bool completed = task.Wait(timeoutMs);
                    if (!completed)
                    {
                        return new TestResult
                        {
                            FullName = fullName,
                            Passed = false,
                            ErrorMessage = $"Test timed out after {timeoutMs} ms."
                        };
                    }
                }

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

    public static TestResult ExecuteWorkItem(TestWorkItem workItem)
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
        if (workItem.TimeoutMilliseconds > 0)
            return RunSingleTestWithTimeout(instance!, workItem.TestClassType, workItem.Method, workItem.FullName, workItem.TimeoutMilliseconds);
        return RunSingleTest(instance!, workItem.TestClassType, workItem.Method, workItem.FullName);
    }
}
