namespace myTestingLibrary.Exceptions
{
    /// <summary>
    /// This class is used to represent an exception that is thrown when there is an issue with the test setup in the testing library.
    /// </summary>
    public class TestSetupException : Exception
    {
        public TestSetupException(string message) : base(message)
        {
            message = "A test setup exception was thrown. " + message;
        }
    }
}
