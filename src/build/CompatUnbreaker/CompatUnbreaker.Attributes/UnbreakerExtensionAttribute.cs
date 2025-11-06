namespace CompatUnbreaker.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class UnbreakerExtensionAttribute : Attribute
{
    public UnbreakerExtensionAttribute(Type type)
    {
    }
}
