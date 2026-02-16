using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;
using System;
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

        private sealed class TestResult
        {
            public string FullName { get; set; } = "";
            public bool Passed { get; set; }
            public string? ErrorMessage { get; set; }
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: myTestRunner <path-to-test-assembly.dll>");
                return;
            }

            string filePath = args[0];
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                Console.WriteLine("Error: Test file not found: " + filePath);
                return;
            }

            string fullPath = Path.GetFullPath(fileInfo.FullName);
            s_testAssemblyDirectory = Path.GetDirectoryName(fullPath);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromTestDirectory;

            Console.WriteLine("Test file found: " + fullPath);
            Console.WriteLine();

            var testStructure = DiscoverTests(fullPath);
            if (testStructure.Count == 0)
            {
                Console.WriteLine("No test classes or methods found in the assembly.");
                return;
            }

            // Register shared context: one InventoryService instance injected into test classes via [SharedContext].
            var sharedInventory = new InventoryService();
            TestContextContainer.RegisterSharedObject(sharedInventory);

            var results = RunAllTests(testStructure);
            PrintSummary(results);
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

        private static List<TestResult> RunAllTests(Dictionary<Type, List<MethodInfo>> testStructure)
        {
            var results = new List<TestResult>();

            foreach (var kv in testStructure.OrderBy(k => k.Key.Name))
            {
                Type testClassType = kv.Key;
                List<MethodInfo> methods = kv.Value;

                object? instance = null;
                try
                {
                    instance = Activator.CreateInstance(testClassType);
                }
                catch (Exception ex)
                {
                    foreach (var method in methods)
                    {
                        string fullName = $"{testClassType.FullName}.{method.Name}";
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Loading test: {fullName}");
                        Console.ResetColor();

                        var failResult = new TestResult { FullName = fullName, Passed = false, ErrorMessage = "Failed to create test class instance: " + ex.Message };
                        results.Add(failResult);
                        PrintTestResult(failResult);
                    }
                    continue;
                }

                InjectSharedContext(instance, testClassType);

                foreach (MethodInfo method in methods)
                {
                    string fullName = $"{testClassType.FullName}.{method.Name}";

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Loading test: {fullName}");
                    Console.ResetColor();

                    var result = RunSingleTest(instance, testClassType, method, fullName);
                    results.Add(result);
                    PrintTestResult(result);
                }
            }

            return results;
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

        private static void PrintTestResult(TestResult result)
        {
            if (result.Passed)
            {
                Console.BackgroundColor = ConsoleColor.Green;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write(" SUCCESS ");
                Console.ResetColor();
                Console.WriteLine("");
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" FAILURE ");
                Console.ResetColor();
                Console.WriteLine("");
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    Console.WriteLine(result.ErrorMessage);
                    Console.ResetColor();
                }
            }
            Console.WriteLine();
        }

        private static void PrintSummary(List<TestResult> results)
        {
            int total = results.Count;
            int passed = results.Count(r => r.Passed);
            int failed = total - passed;

            Console.WriteLine("========== Test run summary ==========");
            Console.WriteLine($"Total tests:  {total}");
            Console.WriteLine($"Passed:       {passed}");
            Console.WriteLine($"Failed:       {failed}");
            Console.WriteLine();
            Console.WriteLine("Tests:");
            foreach (var r in results)
            {
                string status = r.Passed ? " [OK] [v]" : " [FAIL] [x]";
                Console.WriteLine($"  {r.FullName}{status}");
            }
            Console.WriteLine("======================================");
        }
    }
}
