namespace myTestingLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false)]
    public class AfterEachAttribute : Attribute { }
}
