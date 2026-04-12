using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace myTestingLibrary
{
    public static class Assertion
    {
        /// <summary>
        /// This method is used to assert that two values are equal. It takes two parameters, 
        /// expected and actual, and compares them using the default equality comparer for the type T. 
        /// If the values are not equal, it throws an AssertionException with a message that includes the 
        /// expected and actual values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expected"></param>
        /// <param name="actual"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void AreEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exceptions.AssertionException($"Expected: {expected}, Actual: {actual}");
            }
        }

        /// <summary>
        /// This method is used to assert that two values are not equal. It takes two parameters, expected and actual,
        /// and compares them using the default equality comparer for the type T. If the values are equal, 
        /// it throws an AssertionException with a message that includes the expected and actual values.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expected"></param>
        /// <param name="actual"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void AreNotEqual<T>(T expected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exceptions.AssertionException($"Expected: {expected} to not be equal to Actual: {actual}");
            }
        }

        /// <summary>
        /// This method is used to assert that a condition is true. It takes a boolean parameter, condition, and checks if it is true.
        /// </summary>
        /// <param name="condition"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void IsTrue(bool condition)
        {
            if (!condition)
            {
                throw new Exceptions.AssertionException("Expected condition to be true, but it was false.");
            }
        }

        /// <summary>
        /// This method is used to assert that a condition is false. It takes a boolean parameter, condition, and checks if it is false.
        /// </summary>
        /// <param name="condition"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void IsFalse(bool condition)
        {
            if (condition)
            {
                throw new Exceptions.AssertionException("Expected condition to be false, but it was true.");
            }
        }

        /// <summary>
        /// This method is used to assert that an object is null. It takes an object parameter, obj, and checks if it is null.
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void IsNull(object obj)
        {
            if (obj != null)
            {
                throw new Exceptions.AssertionException("Expected object to be null, but it was not.");
            }
        }

        /// <summary>
        /// This method is used to assert that an object is not null. It takes an object parameter, obj, and checks if it is not null.
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void IsNotNull(object obj)
        {
            if (obj == null)
            {
                throw new Exceptions.AssertionException("Expected object to be not null, but it was null.");
            }
        }

        /// <summary>
        /// This method is used to assert that a specific exception is thrown when executing a given action. 
        /// It takes a generic type parameter TException, which specifies the type of exception that is expected to be thrown, 
        /// and an Action parameter, which represents the code that is expected to throw the exception. 
        /// If the expected exception is not thrown, or if a different exception is thrown, it throws an AssertionException with 
        /// a message that indicates the failure. If the expected exception is thrown, the test passes and no exception is thrown.
        /// </summary>
        /// <typeparam name="TException"></typeparam>
        /// <param name="action"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                throw new Exceptions.AssertionException($"Expected exception of type {typeof(TException).Name} to be thrown, but no exception was thrown.");
            }
            catch (TException)
            {
                // Expected exception was thrown, test passes
            }
            catch (Exception ex)
            {
                throw new Exceptions.AssertionException($"Expected exception of type {typeof(TException).Name} to be thrown, but an exception of type {ex.GetType().Name} was thrown instead.");
            }
        }

        /// <summary>
        /// This method is used to assert that no exception is thrown when executing a given action. 
        /// It takes an Action parameter, which represents the code that is expected to not throw any exceptions.
        /// </summary>
        /// <param name="action"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void DoesNotThrow(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new Exceptions.AssertionException($"Expected no exception to be thrown, but an exception of type {ex.GetType().Name} was thrown instead.");
            }
        }

        /// <summary>
        /// This method is used to assert that a collection is empty. It takes a Collection<T> parameter, 
        /// collection, and checks if it is null or has a count of 0.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void IsNotEmpty<T>(Collection<T> collection)
        {
            if (collection == null || collection.Count == 0)
            {
                throw new Exceptions.AssertionException("Expected collection to be not empty, but it was empty or null.");
            }
        }

        /// <summary>
        /// This method is used to assert that an object is of a specific type. 
        /// It takes a Type parameter, type, and an object parameter, obj,
        /// and checks if the object is an instance of the specified type 
        /// using the IsInstanceOfType method of the Type class.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="obj"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void IsInstanceOfType(Type type, object obj)
        {
            if (!type.IsInstanceOfType(obj))
            {
                throw new Exceptions.AssertionException($"Expected object to be of type {type.Name}, but it was of type {obj.GetType().Name}.");
            }
        }

        /// <summary>
        /// This method is used to assert that a string contains a specific substring. 
        /// It takes two string parameters, expectedSubstring and actualString,
        /// and checks if the actualString contains the expected
        /// substring using the Contains method of the string class.
        /// </summary>
        /// <param name="expectedSubstring"></param>
        /// <param name="actualString"></param>
        /// <exception cref="Exceptions.AssertionException"></exception>
        public static void Contains(string expectedSubstring, string actualString)
        {
            if (actualString == null || !actualString.Contains(expectedSubstring))
            {
                throw new Exceptions.AssertionException($"Expected string to contain '{expectedSubstring}', but it was '{actualString}'.");
            }
        }

        /// <summary>
        /// Проверяет истинность выражения. При сбое разбирает дерево выражений 
        /// и выводит детальную информацию об операндах и операции.
        /// </summary>
        public static void That(Expression<Func<bool>> conditionExpr)
        {
            var compiled = conditionExpr.Compile();
            if (!compiled())
            {
                string details = ParseExpression(conditionExpr.Body);
                throw new Exceptions.AssertionException($"Expression failed: {conditionExpr.Body}\nDetails: {details}");
            }
        }

        private static string ParseExpression(Expression expr)
        {
            if (expr is BinaryExpression binExpr)
            {
                object leftVal = GetValue(binExpr.Left);
                object rightVal = GetValue(binExpr.Right);
                string op = binExpr.NodeType.ToString();
                return $"Left operand ({binExpr.Left}) = '{leftVal}', Operator = '{op}', Right operand ({binExpr.Right}) = '{rightVal}'";
            }
            return "Complex or non-binary expression.";
        }

        private static object GetValue(Expression expr)
        {
            try
            {
                // Оборачиваем выражение в конвертацию к object и компилируем для получения значения
                var objectMember = Expression.Convert(expr, typeof(object));
                var getterLambda = Expression.Lambda<Func<object>>(objectMember);
                var getter = getterLambda.Compile();
                return getter();
            }
            catch
            {
                return "Unknown/Un-evaluable";
            }
        }
    }
}
