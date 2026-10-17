// The .NET 6 runtime assemblies we compile against don't expose these compiler helper
// attributes publicly; defining them here lets the C# compiler emit them. Still needed (checked 0.13.75, code
// review M-17f): without them the build fails with CS0656 wherever tuples or generic references are used.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte b) { }
        public NullableAttribute(byte[] b) { }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte b) { }
    }
}
