namespace myTestingLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TestMethodAttribute : Attribute
    {
        public string Description { get; }
        public int Priority { get; }
        public bool Ignore { get; }
        public string ExpectedException { get; }
        public object[] Data { get; }

        public TestMethodAttribute(string description = "", int priority = 0, bool ignore = false,
            string expectedException = "", object[] data = null)
        {
            Description = description;
            Priority = priority;
            Ignore = ignore;
            ExpectedException = expectedException;
            Data = data ?? new object[0];
        }
    }

}
