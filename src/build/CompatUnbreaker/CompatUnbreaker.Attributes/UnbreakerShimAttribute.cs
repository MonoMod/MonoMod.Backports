namespace CompatUnbreaker.Attributes;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class UnbreakerShimAttribute : Attribute
{
    public UnbreakerShimAttribute(string targetAssemblyName)
    {
    }
}
