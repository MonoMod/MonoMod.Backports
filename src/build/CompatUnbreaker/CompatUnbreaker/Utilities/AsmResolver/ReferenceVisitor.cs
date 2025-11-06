using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Models;

namespace CompatUnbreaker.Utilities.AsmResolver;

/// <summary>
/// <see cref="ReferenceImporter"/> but visits everything regardless of whether it was already imported.
/// </summary>
internal abstract class ReferenceVisitor(ModuleDefinition module) : ReferenceImporter(module)
{
    /// <inheritdoc />
    public override IResolutionScope ImportScope(IResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope switch
        {
            AssemblyReference assembly => ImportAssembly(assembly),
            TypeReference parentType => (IResolutionScope) ImportType(parentType),
            ModuleDefinition moduleDef => ImportAssembly(moduleDef.Assembly ?? throw new ArgumentException("Module is not added to an assembly.")),
            ModuleReference moduleRef => ImportModule(moduleRef),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }

    /// <inheritdoc />
    public override IImplementation ImportImplementation(IImplementation? implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);

        return implementation switch
        {
            AssemblyReference assembly => ImportAssembly(assembly),
            ExportedType type => ImportType(type),
            FileReference file => ImportFile(file),
            _ => throw new ArgumentOutOfRangeException(nameof(implementation)),
        };
    }

    /// <inheritdoc />
    protected override ITypeDefOrRef ImportType(TypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (((ITypeDescriptor) type).Scope is not { } scope)
            throw new ArgumentException("Cannot import a type that has not been added to a module.");

        return new TypeReference(
            TargetModule,
            ImportScope(scope),
            type.Namespace,
            type.Name);
    }

    /// <inheritdoc />
    protected override ITypeDefOrRef ImportType(TypeReference type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new TypeReference(
            TargetModule,
            type.Scope is not null
                ? ImportScope(type.Scope)
                : null,
            type.Namespace,
            type.Name);
    }

    /// <inheritdoc />
    protected override ITypeDefOrRef ImportType(TypeSpecification type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Signature is null)
            throw new ArgumentNullException(nameof(type));

        return new TypeSpecification(ImportTypeSignature(type.Signature));
    }

    /// <inheritdoc />
    public override ExportedType ImportType(ExportedType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var result = TargetModule.ExportedTypes.FirstOrDefault(a => SignatureComparer.Default.Equals(a, type));

        if (result is null)
        {
            result = new ExportedType(ImportImplementation(type.Implementation), type.Namespace, type.Name);
            TargetModule.ExportedTypes.Add(result);
        }

        return result;
    }

    /// <inheritdoc />
    public override TypeSignature ImportTypeSignature(TypeSignature type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.AcceptVisitor(this);
    }

    /// <inheritdoc />
    public override IMethodDefOrRef ImportMethod(IMethodDefOrRef method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.DeclaringType is null)
            throw new ArgumentException("Cannot import a method that is not added to a type.");
        if (method.Signature is null)
            throw new ArgumentException("Cannot import a method that does not have a signature.");

        return new MemberReference(
            ImportType(method.DeclaringType),
            method.Name,
            ImportMethodSignature(method.Signature));
    }

    /// <inheritdoc />
    public override MethodSpecification ImportMethod(MethodSpecification method)
    {
        if (method.Method is null || method.Signature is null)
            throw new ArgumentNullException(nameof(method));
        if (method.DeclaringType is null)
            throw new ArgumentException("Cannot import a method that is not added to a type.");

        var memberRef = ImportMethod(method.Method);
        var signature = ImportGenericInstanceMethodSignature(method.Signature);

        return new MethodSpecification(memberRef, signature);
    }

    /// <inheritdoc />
    public override IFieldDescriptor ImportField(IFieldDescriptor field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.DeclaringType is null)
            throw new ArgumentException("Cannot import a field that is not added to a type.");
        if (field.Signature is null)
            throw new ArgumentException("Cannot import a field that does not have a signature.");

        return new MemberReference(
            ImportType((ITypeDefOrRef) field.DeclaringType),
            field.Name,
            ImportFieldSignature(field.Signature));
    }
}
