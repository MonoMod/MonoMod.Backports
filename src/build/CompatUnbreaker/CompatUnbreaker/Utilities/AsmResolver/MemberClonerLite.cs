using AsmResolver.DotNet;
using AsmResolver.DotNet.Cloning;
using AsmResolver.DotNet.Signatures;

namespace CompatUnbreaker.Utilities.AsmResolver;

/// <summary>
/// Methods extracted from <see cref="MemberCloner"/> to allow for more flexibility.
/// </summary>
internal readonly partial struct MemberClonerLite
{
    private readonly ReferenceImporter _importer;

    public MemberClonerLite(ReferenceImporter importer)
    {
        _importer = importer;
    }

    public MemberClonerLite(ModuleDefinition targetModule) : this(targetModule.DefaultImporter)
    {
    }

    private ImplementationMap? CloneImplementationMap(ImplementationMap? map)
    {
        if (map is null)
            return null;
        if (map.Scope is null)
            throw new ArgumentException($"Scope of implementation map {map} is null.");

        return new ImplementationMap(map.Scope.ImportWith(_importer), map.Name, map.Attributes);
    }

    private static Constant? CloneConstant(Constant? constant)
    {
        return constant is not null
            ? new Constant(
                constant.Type,
                constant.Value is null
                    ? null
                    : new DataBlobSignature(constant.Value.Data)
            )
            : null;
    }
}
