namespace CompatUnbreaker.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class UnbreakerRenameAttribute : Attribute
{
    public UnbreakerRenameAttribute(string namespaceName, string newNamespaceName)
    {
    }

    public UnbreakerRenameAttribute(Type type, string newTypeName)
    {
    }

    public UnbreakerRenameAttribute(Type type, string memberName, string newMemberName)
    {
    }
}

// TODO maybe switch to this?
public static class UnbreakerRename
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class NamespaceAttribute : Attribute
    {
        public NamespaceAttribute(string name, string newName)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TypeAttribute : Attribute
    {
        public TypeAttribute(Type type, string newName)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TypeMemberAttribute : Attribute
    {
        public TypeMemberAttribute(Type type, string memberName, string newMemberName)
        {
        }
    }
}
