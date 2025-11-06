using System.Diagnostics;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using CompatUnbreaker.Models;
using CompatUnbreaker.Models.Attributes;
using CompatUnbreaker.Processors.Abstractions;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Processors;

internal sealed class UnbreakerReferenceProcessor : IReferenceProcessor
{
    public void Process(ProcessorContext context, AssemblyDefinition referenceAssembly)
    {
        if (context.ShimModel.TargetAssembly != referenceAssembly)
        {
            throw new InvalidOperationException("Shim model target assembly does not match the reference assembly.");
        }

        var referenceModule = referenceAssembly.ManifestModule!;

        var importer = new RedirectReferenceImporter(referenceModule);
        importer.Assemblies.Add(context.ShimModel.ShimAssembly, context.ShimModel.TargetAssembly);

        foreach (var type in context.ShimModel.AllTypes)
        {
            if (type.Kind == ShimTypeKind.Replace)
            {
                importer.Types.Add(type.Definition, type.TargetDescriptor);
            }
        }

        var memberCloner = new MemberClonerLite(importer);

        foreach (var rename in context.ShimModel.Renames.OfType<RenameData.MemberRename>())
        {
            var typeDefinition = rename.Type.Resolve();

            // TODO other members
            typeDefinition.Methods.Single(m => m.Name == rename.NewMemberName).Name = rename.MemberName;
        }

        var targetTypes = new Dictionary<ShimTypeModel, TypeDefinition>();

        foreach (var shimTypeModel in context.ShimModel.AllTypes)
        {
            var targetType = shimTypeModel.TargetDescriptor.Resolve();
            if (targetType != null && !targetType.IsVisibleOutsideOfAssembly())
            {
                targetType = null;
            }

            var shimType = shimTypeModel.Definition;

            if (shimTypeModel.Kind == ShimTypeKind.Replace && targetType != null)
            {
                if (targetType.DeclaringType is { } targetDeclaringType)
                {
                    targetDeclaringType.NestedTypes.Remove(targetType);
                }
                else
                {
                    referenceModule.TopLevelTypes.Remove(targetType);
                }

                targetType = null;
            }

            if (targetType == null)
            {
                targetType = memberCloner.CloneType(shimType);
                targetType.Namespace = shimTypeModel.TargetDescriptor.Namespace;
                targetType.Name = shimTypeModel.TargetDescriptor.Name;
                targetTypes.Add(shimTypeModel, targetType);

                if (shimTypeModel.TargetDescriptor.DeclaringType is { } shimDeclaringTypeDescriptor)
                {
                    if (shimTypeModel.DeclaringType != null && targetTypes.TryGetValue(shimTypeModel.DeclaringType, out var shimDeclaringType))
                    {
                        Debug.Assert(SignatureComparer.Default.Equals(shimDeclaringType, shimDeclaringTypeDescriptor));
                    }
                    else
                    {
                        shimDeclaringType = shimDeclaringTypeDescriptor.Resolve()
                                            ?? throw new InvalidOperationException($"Could not resolve declaring type for {shimTypeModel}.");
                    }

                    shimDeclaringType.NestedTypes.Add(targetType);
                }
                else
                {
                    referenceModule.TopLevelTypes.Add(targetType);
                }
            }

            var clonedMethods = new Dictionary<IMemberDescriptor, MethodDefinition>();

            foreach (var methodModel in shimTypeModel.Methods)
            {
                var targetMethod = memberCloner.CloneMethod(methodModel.Definition);
                targetMethod.Name = methodModel.TargetDescriptor.Name;
                targetMethod.Signature = (MethodSignature) ((MethodSignature) methodModel.TargetDescriptor.Signature).ImportWith(importer);

                if (targetMethod.Name == ".ctor")
                {
                    targetMethod.IsSpecialName = targetMethod.IsRuntimeSpecialName = true;
                }

                if (methodModel.Definition.CilMethodBody != null)
                {
                    var body = targetMethod.CilMethodBody = new CilMethodBody();
                    body.Instructions.Add(CilOpCodes.Ldnull);
                    body.Instructions.Add(CilOpCodes.Throw);
                }

                targetType.Methods.Add(targetMethod);
                clonedMethods.Add(methodModel.Definition, targetMethod);
            }

            foreach (var shimFieldModel in shimTypeModel.Fields)
            {
                FieldDefinition targetField;
                switch (shimFieldModel)
                {
                    case ShimFieldModel.FromField fromField:
                        targetField = memberCloner.CloneField(fromField.Definition);
                        targetField.Name = fromField.TargetDescriptor.Name;
                        targetField.Signature = ((FieldSignature) fromField.TargetDescriptor.Signature).ImportWith(importer);
                        break;

                    case ShimFieldModel.FromProperty fromProperty:
                        var shimProperty = fromProperty.Definition;
                        targetField = new FieldDefinition(fromProperty.TargetDescriptor.Name, default, ((FieldSignature) fromProperty.TargetDescriptor.Signature).ImportWith(importer))
                        {
                            IsPublic = true,
                            IsInitOnly = shimProperty.SetMethod == null,
                        };
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(shimFieldModel));
                }

                targetType.Fields.Add(targetField);
            }

            foreach (var shimPropertyModel in shimTypeModel.Properties)
            {
                var targetProperty = memberCloner.CloneProperty(shimPropertyModel.Definition, clonedMethods);

                targetType.Properties.Add(targetProperty);
            }

            foreach (var shimEventModel in shimTypeModel.Events)
            {
                var targetEvent = memberCloner.CloneEvent(shimEventModel.Definition, clonedMethods);

                targetType.Events.Add(targetEvent);
            }
        }

        Strip(referenceModule);
    }

    private static void ConvertExtensionMethodToInstance(MethodDefinition targetMethod)
    {
        if (!targetMethod.IsStatic)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(targetMethod.Signature);

        if (targetMethod.GetExtensionAttribute() is { } extensionAttribute)
        {
            targetMethod.CustomAttributes.Remove(extensionAttribute);
        }

        targetMethod.IsStatic = false;
        targetMethod.Signature.ParameterTypes.RemoveAt(0);
        targetMethod.Signature.HasThis = true;

        if (targetMethod.Parameters.ThisParameter?.Definition is { } thisParameterDefinition)
        {
            targetMethod.ParameterDefinitions.Remove(thisParameterDefinition);
        }

        foreach (var parameterDefinition in targetMethod.ParameterDefinitions)
        {
            parameterDefinition.Sequence--;
        }

        targetMethod.Parameters.PullUpdatesFromMethodSignature();
    }

    private static void Strip(ModuleDefinition module)
    {
        foreach (var method in module.GetAllTypes().SelectMany(t => t.Methods))
        {
            if (method.CilMethodBody != null)
            {
                var body = method.CilMethodBody = new CilMethodBody();
                body.Instructions.Add(CilOpCodes.Ldnull);
                body.Instructions.Add(CilOpCodes.Throw);
            }
        }
    }

    private sealed class RedirectReferenceImporter(ModuleDefinition module) : ReferenceImporter(module)
    {
        public Dictionary<AssemblyDescriptor, AssemblyDescriptor> Assemblies { get; } = new(SignatureComparer.VersionAgnostic);
        public Dictionary<ITypeDefOrRef, ITypeDefOrRef> Types { get; } = new(SignatureComparer.VersionAgnostic);

        protected override AssemblyReference ImportAssembly(AssemblyDescriptor assembly)
        {
            if (Assemblies.TryGetValue(assembly, out var redirected))
            {
                assembly = redirected;
            }

            return base.ImportAssembly(assembly);
        }

        protected override ITypeDefOrRef ImportType(TypeReference type)
        {
            if (Types.TryGetValue(type, out var redirected))
            {
                return base.ImportType(redirected);
            }

            return base.ImportType(type);
        }

        protected override ITypeDefOrRef ImportType(TypeDefinition type)
        {
            if (Types.TryGetValue(type, out var redirected))
            {
                return base.ImportType(redirected);
            }

            return base.ImportType(type);
        }
    }
}
