namespace myTestingLibrary.Exceptions
{
    /// <summary>
    /// This class is used to represent an exception that is thrown when an assertion fails in the testing library. 
    /// It inherits from the base Exception class and provides a constructor that takes a message as a parameter, 
    /// which can be used to provide more information about the assertion failure.
    /// </summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message)
        {
            message = "An assertion exception was thrown. " + message;
        }
    }
}
