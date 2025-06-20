namespace XactJobs
{
    internal readonly struct XactJobDispatchKey : IEquatable<XactJobDispatchKey>
    {
        public string TypeName { get; }
        public string MethodName { get; }
        public int ArgCount { get; }

        public XactJobDispatchKey(string typeName, string methodName, int argCount)
        {
            TypeName = typeName;
            MethodName = methodName;
            ArgCount = argCount;
        }

        public bool Equals(XactJobDispatchKey other) =>
            TypeName == other.TypeName && MethodName == other.MethodName && ArgCount == other.ArgCount;

        public override bool Equals(object? obj) => obj is XactJobDispatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TypeName, MethodName, ArgCount);
    }

}
