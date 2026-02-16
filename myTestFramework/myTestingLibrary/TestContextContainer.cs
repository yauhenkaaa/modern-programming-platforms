namespace myTestingLibrary
{
    public static class TestContextContainer
    {
        private static Dictionary<Type, object> _sharedObjects;

        public static void RegisterSharedObject<T>(T instance)
        {
            if (_sharedObjects == null)
                _sharedObjects = new Dictionary<Type, object>();
            _sharedObjects[typeof(T)] = instance;
        }

        public static T Get<T>()
        {
            if (_sharedObjects == null || !_sharedObjects.TryGetValue(typeof(T), out object? value))
                throw new InvalidOperationException($"No object of type {typeof(T).FullName} has been registered.");
            return (T)value;
        }


    }
}
