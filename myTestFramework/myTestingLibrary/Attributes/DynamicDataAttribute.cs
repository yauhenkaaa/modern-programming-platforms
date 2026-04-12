using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace myTestingLibrary.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class DynamicDataAttribute : Attribute
    {
        public string MethodName { get; }

        public DynamicDataAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
