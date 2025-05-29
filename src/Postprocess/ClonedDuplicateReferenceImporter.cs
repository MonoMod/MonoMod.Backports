using AsmResolver.DotNet;
using AsmResolver.DotNet.Cloning;

namespace Postprocess;

internal sealed class ClonedDuplicateReferenceImporter : CloneContextAwareReferenceImporter
{
    private readonly ModuleDefinition targetModule;

    public ClonedDuplicateReferenceImporter(
        ModuleDefinition targetModule,
        MemberCloneContext context) : base(context)
    {
        this.targetModule = targetModule;
    }

    protected override ITypeDefOrRef ImportType(TypeDefinition type)
    {
        if (Context.ClonedMembers.TryGetValue(type, out var clonedType))
        {
            return (ITypeDefOrRef)clonedType;
        }
        else if (targetModule.TopLevelTypes.FirstOrDefault(t => t.Namespace == type.Namespace && t.Name == type.Name) is { } existing)
        {
            return existing;
        }
        else
        {
            return base.ImportType(type);
        }
    }
}
