namespace myTestingLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false)]
    public class BeforeEachAttribute : Attribute { }
}
