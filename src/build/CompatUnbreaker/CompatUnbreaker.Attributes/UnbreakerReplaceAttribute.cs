namespace CompatUnbreaker.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class UnbreakerReplaceAttribute : Attribute
{
    public UnbreakerReplaceAttribute(Type type)
    {
    }
}
