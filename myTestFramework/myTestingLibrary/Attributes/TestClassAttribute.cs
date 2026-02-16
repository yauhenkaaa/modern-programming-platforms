namespace myTestingLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]    
    public class TestClassAttribute : Attribute
    {
        public string Description { get; }
        public string Category { get; }
        public int Timeout { get; }

        public TestClassAttribute(string description = "", string category = "", int timeout = 0)
        {
            Description = description;
            Category = category;
            Timeout = timeout;
        }
    }

}
