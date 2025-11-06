using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Attributes;
using CompatUnbreaker.Models.Attributes;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Models;

internal sealed class ShimModel
{
    public required AssemblyDefinition ShimAssembly { get; init; }
    public required AssemblyDefinition TargetAssembly { get; init; }

    public required RenameData[] Renames { get; init; }
    public required ShimTypeModel[] AllTypes { get; init; }
    public required Dictionary<IMethodDefOrRef, IMethodDefOrRef> ExtensionImplementations { get; init; }

    public static ShimModel From(AssemblyDefinition shimAssembly)
    {
        var shimModule = shimAssembly.Modules.Single();

        var targetAssemblyName = shimAssembly.Find(AttributeDescription.UnbreakerShimAttribute).SingleOrDefault()
                                 ?? throw new ArgumentException("Provided shim assembly doesn't have the required UnbreakerShimAttribute.", nameof(shimAssembly));

        var targetAssemblyReference = shimModule.AssemblyReferences.SingleOrDefault(a => a.Name == targetAssemblyName)
                                      ?? new AssemblyReference(targetAssemblyName, new Version());

        var targetAssembly = shimModule.MetadataResolver.AssemblyResolver.Resolve(targetAssemblyReference)
                             ?? throw new ArgumentException($"Could not resolve target assembly '{targetAssemblyReference}'.");

        var targetModule = targetAssembly.Modules.Single();

        var renames = shimAssembly.Find(AttributeDescription.UnbreakerRenameAttribute).ToArray();

        var extensionImplementations = new Dictionary<IMethodDefOrRef, IMethodDefOrRef>(SignatureComparer.Default);

        var typeModels = new List<ShimTypeModel>();

        var queue = new Queue<(ShimTypeModel? DeclaringType, TypeDefinition Type)>(
            shimModule.TopLevelTypes.Select(t => ((ShimTypeModel?) null, t))
        );

        while (queue.Count > 0)
        {
            var (declaringType, shimType) = queue.Dequeue();

            var hasExtensionsAttribute = shimType.Has(AttributeDescription.UnbreakerExtensionsAttribute);
            if (hasExtensionsAttribute)
            {
                foreach (var shimNestedType in shimType.NestedTypes)
                {
                    queue.Enqueue((null, shimNestedType));
                }

                continue;
            }

            ShimTypeKind kind;
            ITypeDefOrRef targetReference;
            TypeSignature? extensionParameter = null;

            if (shimType.TryGetExtensionMarkerMethod() is { } markerMethod && shimType.DeclaringType.Has(AttributeDescription.UnbreakerExtensionsAttribute))
            {
                kind = ShimTypeKind.NativeExtension;
                extensionParameter = markerMethod.Signature!.ParameterTypes.Single();
                targetReference = extensionParameter.GetUnderlyingTypeDefOrRef();
                // TODO validate target type generics match shim type generics 1 to 1
            }
            else if (shimType.TryFindSingle(AttributeDescription.UnbreakerExtensionAttribute, out var extensionAttributeTarget))
            {
                kind = ShimTypeKind.UnbreakerExtension;
                targetReference = ((TypeDefOrRefSignature) extensionAttributeTarget!).Type;
            }
            else if (shimType.TryFindSingle(AttributeDescription.UnbreakerReplaceAttribute, out var replaceAttributeTarget))
            {
                kind = ShimTypeKind.Replace;
                targetReference = ((TypeDefOrRefSignature) replaceAttributeTarget!).Type;
            }
            else
            {
                kind = ShimTypeKind.New;

                IResolutionScope scope = declaringType == null
                    ? targetModule
                    : declaringType.TargetDescriptor switch
                    {
                        TypeReference reference => reference,
                        TypeDefinition definition => definition.ToTypeReference(),
                        _ => throw new ArgumentOutOfRangeException(),
                    };

                targetReference = new TypeReference(shimModule, scope, shimType.Namespace, shimType.Name);
            }

            if (!shimType.IsVisibleOutsideOfAssembly())
            {
                if (kind == ShimTypeKind.New)
                {
                    continue;
                }

                throw new InvalidOperationException($"Shim type '{shimType.FullName}' isn't public.");
            }

            if (targetReference.Scope?.GetAssembly()?.Name != targetAssembly.Name)
            {
                throw new InvalidOperationException($"Shim type '{shimType.FullName}' targets type '{targetReference}', which is not in the target assembly.");
            }

            var targetType = targetReference.Resolve();
            if (targetType != null && !targetType.IsVisibleOutsideOfAssembly())
            {
                targetType = null;
            }

            if (kind == ShimTypeKind.New && targetType != null)
            {
                throw new InvalidOperationException($"Shim type '{shimType.FullName}' conflicts with target type and doesn't specify the {nameof(UnbreakerReplaceAttribute)}.");
            }

            if (kind != ShimTypeKind.New && targetType == null)
            {
                throw new InvalidOperationException($"Shim type '{shimType.FullName}' target type '{targetReference}' could not be resolved.");
            }

            if (kind == ShimTypeKind.UnbreakerExtension && !shimType.IsStatic())
            {
                throw new InvalidOperationException($"Extension shim type '{shimType.FullName}' is not static.");
            }

            var members = new List<IShimMemberModel>();

            var ignoredMethods = new HashSet<MethodDefinition>();

            foreach (var shimProperty in shimType.Properties)
            {
                if (!shimProperty.IsVisibleOutsideOfAssembly()) continue;

                var hasFieldAttribute = shimProperty.Has(AttributeDescription.UnbreakerFieldAttribute);

                if (hasFieldAttribute)
                {
                    var fieldType = shimProperty.Signature!.ReturnType;
                    members.Add(new ShimFieldModel.FromProperty
                    {
                        TargetDescriptor = new MemberReference(targetReference, shimProperty.Name, new FieldSignature(fieldType)),
                        Definition = shimProperty,
                    });

                    if (shimProperty.GetMethod != null) ignoredMethods.Add(shimProperty.GetMethod);
                    if (shimProperty.SetMethod != null) ignoredMethods.Add(shimProperty.SetMethod);
                }
                else
                {
                    members.Add(new ShimPropertyModel
                    {
                        Definition = shimProperty,
                    });
                }
            }

            foreach (var shimEvent in shimType.Events)
            {
                if (!shimEvent.IsVisibleOutsideOfAssembly()) continue;

                members.Add(new ShimEventModel
                {
                    Definition = shimEvent,
                });
            }

            foreach (var shimMethod in shimType.Methods)
            {
                if (!shimMethod.IsVisibleOutsideOfAssembly()) continue;

                if (extensionParameter != null)
                {
                    var implementationMethod = shimType.FindCorrespondingExtensionImplementationMethod(shimMethod, extensionParameter)
                                               ?? throw new InvalidOperationException($"Couldn't find corresponding implementation method for {shimMethod}.");

                    extensionImplementations.Add(shimMethod, implementationMethod);
                }

                if (ignoredMethods.Contains(shimMethod)) continue;

                var isConstructor = shimMethod.Has(AttributeDescription.UnbreakerConstructorAttribute);

                members.Add(new ShimMethodModel
                {
                    IsUnbreakerConstructor = isConstructor,
                    TargetDescriptor = isConstructor
                        ? new MemberReference(targetReference, ".ctor"u8, MethodSignature.CreateInstance(targetReference.ContextModule.CorLibTypeFactory.Void, shimMethod.Signature.ParameterTypes))
                        : new MemberReference(targetReference, shimMethod.Name, shimMethod.Signature),
                    Definition = shimMethod,
                });
            }

            foreach (var shimField in shimType.Fields)
            {
                if (!shimField.IsVisibleOutsideOfAssembly()) continue;

                members.Add(new ShimFieldModel.FromField
                {
                    TargetDescriptor = new MemberReference(targetReference, shimField.Name, shimField.Signature),
                    Definition = shimField,
                });
            }

            var typeModel = new ShimTypeModel
            {
                Kind = kind,
                DeclaringType = declaringType,
                TargetDescriptor = targetReference,
                Members = members.ToArray(),
                Definition = shimType,
            };

            typeModels.Add(typeModel);

            foreach (var shimNestedType in shimType.NestedTypes)
            {
                queue.Enqueue((typeModel, shimNestedType));
            }
        }

        return new ShimModel
        {
            ShimAssembly = shimAssembly,
            TargetAssembly = targetAssembly,
            Renames = renames,
            AllTypes = typeModels.ToArray(),
            ExtensionImplementations = extensionImplementations,
        };
    }
}

internal enum ShimTypeKind
{
    New,
    Replace,
    NativeExtension,
    UnbreakerExtension,
}

internal interface IShimMemberModel
{
    IMemberDescriptor TargetDescriptor { get; }
}

internal sealed class ShimTypeModel : IShimMemberModel
{
    public required ShimTypeKind Kind { get; init; }

    public required ShimTypeModel? DeclaringType { get; init; }
    public required ITypeDefOrRef TargetDescriptor { get; init; }
    IMemberDescriptor IShimMemberModel.TargetDescriptor => TargetDescriptor;

    public required IShimMemberModel[] Members { get; init; }

    public IEnumerable<ShimMethodModel> Methods => Members.OfType<ShimMethodModel>();
    public IEnumerable<ShimFieldModel> Fields => Members.OfType<ShimFieldModel>();
    public IEnumerable<ShimPropertyModel> Properties => Members.OfType<ShimPropertyModel>();
    public IEnumerable<ShimEventModel> Events => Members.OfType<ShimEventModel>();

    public required TypeDefinition Definition { get; init; }

    public override string ToString()
    {
        return $"{Kind} : {TargetDescriptor}";
    }
}

internal sealed class ShimMethodModel : IShimMemberModel
{
    public required MemberReference TargetDescriptor { get; init; }
    IMemberDescriptor IShimMemberModel.TargetDescriptor => TargetDescriptor;

    public required bool IsUnbreakerConstructor { get; init; }
    public required MethodDefinition Definition { get; init; }
}

internal abstract class ShimFieldModel : IShimMemberModel
{
    public required MemberReference TargetDescriptor { get; init; }
    IMemberDescriptor IShimMemberModel.TargetDescriptor => TargetDescriptor;

    internal sealed class FromField : ShimFieldModel
    {
        public required FieldDefinition Definition { get; init; }
    }

    internal sealed class FromProperty : ShimFieldModel
    {
        public required PropertyDefinition Definition { get; init; }
    }
}

internal sealed class ShimPropertyModel : IShimMemberModel
{
    IMemberDescriptor IShimMemberModel.TargetDescriptor => throw new NotSupportedException();

    public required PropertyDefinition Definition { get; init; }
}

internal sealed class ShimEventModel : IShimMemberModel
{
    IMemberDescriptor IShimMemberModel.TargetDescriptor => throw new NotSupportedException();

    public required EventDefinition Definition { get; init; }
}
