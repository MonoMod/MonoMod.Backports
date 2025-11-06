using System.Diagnostics;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using CompatUnbreaker.Models;
using CompatUnbreaker.Models.Attributes;
using CompatUnbreaker.Processors.Abstractions;
using CompatUnbreaker.Utilities.AsmResolver;

namespace CompatUnbreaker.Processors;

internal sealed class UnbreakerConsumerProcessor : IConsumerProcessor
{
    public void Process(ProcessorContext context, AssemblyDefinition consumerAssembly)
    {
        var shimModel = context.ShimModel;
        var consumerModule = consumerAssembly.ManifestModule!;

        var memberRenames = shimModel.Renames.OfType<RenameData.MemberRename>()
            .GroupBy(r => r.Type, SignatureComparer.Default)
            .ToDictionary(
                x => x.Key.ToTypeDefOrRef(),
                g => g.ToDictionary(r => r.MemberName, r => r.NewMemberName),
                SignatureComparer.Default
            );

        var shimMembers = new Dictionary<IMemberDescriptor, IShimMemberModel>(SignatureComparer.VersionAgnostic);

        foreach (var type in shimModel.AllTypes)
        {
            if (type.Kind is ShimTypeKind.New or ShimTypeKind.Replace)
            {
                shimMembers.Add(type.TargetDescriptor, type);
            }

            foreach (var member in type.Members)
            {
                if (member is ShimPropertyModel or ShimEventModel) continue;
                shimMembers.Add(member.TargetDescriptor, member);
            }
        }

        var shimmingImporter = new ShimmingReferenceImporter(consumerModule, shimModel, shimMembers, memberRenames);
        var memberShimmer = new MemberShimmer(shimmingImporter, shimMembers, memberRenames);

        foreach (var type in consumerModule.GetAllTypes())
        {
            memberShimmer.Visit(type);
        }
    }
}

internal sealed class MemberShimmer(
    ShimmingReferenceImporter importer,
    Dictionary<IMemberDescriptor, IShimMemberModel> shimMembers,
    Dictionary<ITypeDefOrRef, Dictionary<Utf8String, Utf8String>> memberRenames
) : ShimmerImporter(importer)
{
    protected override void VisitMethod(MethodDefinition method)
    {
        if (method is { IsVirtual: true, IsNewSlot: false })
        {
            var baseType = method.DeclaringType!;

            while (true)
            {
                baseType = baseType.BaseType?.Resolve();
                if (baseType == null) break;

                if (memberRenames.TryGetValue(baseType, out var renames))
                {
                    if (renames.TryGetValue(method.Name, out var newName))
                    {
                        method.Name = newName;
                    }
                }
            }
        }

        base.VisitMethod(method);
    }

    protected override void VisitCilInstruction(CilMethodBody body, int index, CilInstruction instruction)
    {
        var instructions = body.Instructions;

        if (instruction.OpCode.OperandType is CilOperandType.InlineField && instruction.Operand is IFieldDescriptor field)
        {
            var opCode = instruction.OpCode.Code;

            if (shimMembers.TryGetValue(field, out var shimMember))
            {
                var shimField = (ShimFieldModel) shimMember;

                switch (shimField)
                {
                    case ShimFieldModel.FromField fromField:
                    {
                        instruction.Operand = fromField.Definition.ImportWith(importer);
                        break;
                    }

                    case ShimFieldModel.FromProperty fromProperty:
                    {
                        var accessor = opCode switch
                        {
                            CilCode.Ldfld or CilCode.Ldsfld or CilCode.Ldflda or CilCode.Ldsflda => fromProperty.Definition.GetMethod,
                            CilCode.Stfld or CilCode.Stsfld => fromProperty.Definition.SetMethod,
                            _ => throw new ArgumentOutOfRangeException(),
                        };

                        instruction.ReplaceWith(CilOpCodes.Call, accessor.ImportWith(importer));

                        // Create a temporary local for getting dummy field address
                        if (opCode is CilCode.Ldflda or CilCode.Ldsflda)
                        {
                            var temporaryLocal = new CilLocalVariable(field.Signature.FieldType.ImportWith(importer));
                            body.LocalVariables.Add(temporaryLocal);
                            instructions.Insert(++index, CilOpCodes.Stloc, temporaryLocal);
                            instructions.Insert(++index, CilOpCodes.Ldloca, temporaryLocal);

                            // TODO we could have a simple heuristic here that warns if the next instruction is not a call to a readonly method
                        }

                        break;
                    }

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return;
            }
        }

        base.VisitCilInstruction(body, index, instruction);
    }
}

internal sealed class ShimmingReferenceImporter(
    ModuleDefinition module,
    ShimModel shimModel,
    Dictionary<IMemberDescriptor, IShimMemberModel> shimMembers,
    Dictionary<ITypeDefOrRef, Dictionary<Utf8String, Utf8String>> memberRenames
) : ReferenceVisitor(module)
{
    public override IMethodDefOrRef ImportMethod(IMethodDefOrRef method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.DeclaringType is null)
            throw new ArgumentException("Cannot import a method that is not added to a type.");
        if (method.Signature is null)
            throw new ArgumentException("Cannot import a method that does not have a signature.");

        if (shimModel.ExtensionImplementations.TryGetValue(method, out var implementation))
        {
            return ImportMethod(implementation);
        }

        var signature = ImportMethodSignature(method.Signature);

        var name = method.Name;
        ArgumentNullException.ThrowIfNull(name);

        if (shimMembers.TryGetValue(new MemberReference(method.DeclaringType, name, signature), out var shimMember))
        {
            return ImportMethod(((ShimMethodModel) shimMember).Definition);
        }

        if (memberRenames.TryGetValue(method.DeclaringType.ToTypeSignature().GetUnderlyingTypeDefOrRef(), out var renames) &&
            renames.TryGetValue(name, out var newName))
        {
            name = newName;
        }

        return new MemberReference(ImportType(method.DeclaringType), name, signature);
    }

    public override MethodSpecification ImportMethod(MethodSpecification method)
    {
        if (shimModel.ExtensionImplementations.TryGetValue(method.Method, out var implementation))
        {
            throw new NotImplementedException();
            return base.ImportMethod(method);
        }

        return base.ImportMethod(method);
    }

    protected override ITypeDefOrRef ImportType(TypeReference type)
    {
        if (shimMembers.TryGetValue(type, out var shimMember))
        {
            return base.ImportType(((ShimTypeModel) shimMember).Definition);
        }

        return base.ImportType(type);
    }
}

internal class ShimmerImporter(ReferenceImporter importer)
{
    public virtual void Visit(TypeDefinition type)
    {
        type.BaseType = type.BaseType?.ImportWith(importer);

        foreach (var implementation in type.Interfaces)
        {
            implementation.Interface = implementation.Interface?.ImportWith(importer);
            VisitCustomAttributes(implementation);
        }

        for (var i = 0; i < type.MethodImplementations.Count; i++)
        {
            var implementation = type.MethodImplementations[i];
            type.MethodImplementations[i] = new MethodImplementation(
                implementation.Declaration?.ImportWith(importer),
                implementation.Body?.ImportWith(importer)
            );
        }

        VisitCustomAttributes(type);
        VisitGenericParameters(type);

        foreach (var field in type.Fields)
        {
            field.Signature = field.Signature?.ImportWith(importer);
            VisitCustomAttributes(field);
        }

        foreach (var method in type.Methods)
        {
            VisitMethod(method);
        }

        foreach (var property in type.Properties)
        {
            property.Signature = property.Signature?.ImportWith(importer);
        }

        foreach (var @event in type.Events)
        {
            @event.EventType = @event.EventType?.ImportWith(importer);
        }
    }

    private void VisitCustomAttributes(IHasCustomAttribute provider)
    {
        foreach (var attribute in provider.CustomAttributes)
        {
            attribute.Constructor = (ICustomAttributeType?) attribute.Constructor?.ImportWith(importer);

            if (attribute.Signature is { } signature)
            {
                foreach (var argument in signature.FixedArguments)
                {
                    VisitCustomAttributeArgument(argument);
                }

                foreach (var argument in signature.NamedArguments)
                {
                    VisitCustomAttributeArgument(argument.Argument);
                }
            }
        }
    }

    private void VisitCustomAttributeArgument(CustomAttributeArgument argument)
    {
        argument.ArgumentType = argument.ArgumentType.ImportWith(importer);
        for (var i = 0; i < argument.Elements.Count; i++)
        {
            var element = argument.Elements[i];
            if (element is TypeSignature typeSignature)
            {
                argument.Elements[i] = typeSignature.ImportWith(importer);
            }
        }
    }

    private void VisitGenericParameters(IHasGenericParameters provider)
    {
        foreach (var parameter in provider.GenericParameters)
        {
            foreach (var constraint in parameter.Constraints)
            {
                constraint.Constraint = constraint.Constraint?.ImportWith(importer);
                VisitCustomAttributes(constraint);
            }

            VisitCustomAttributes(parameter);
        }
    }

    protected virtual void VisitMethod(MethodDefinition method)
    {
        method.Signature = method.Signature?.ImportWith(importer);

        VisitCustomAttributes(method);
        VisitGenericParameters(method);

        foreach (var parameterDefinition in method.ParameterDefinitions)
        {
            VisitCustomAttributes(parameterDefinition);
        }

        if (method.CilMethodBody is { } body)
        {
            foreach (var localVariable in body.LocalVariables)
            {
                localVariable.VariableType = localVariable.VariableType.ImportWith(importer);
            }

            foreach (var exceptionHandler in body.ExceptionHandlers)
            {
                exceptionHandler.ExceptionType = exceptionHandler.ExceptionType?.ImportWith(importer);
            }

            var instructions = body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                VisitCilInstruction(body, i, instructions[i]);
            }
        }
    }

    protected virtual void VisitCilInstruction(CilMethodBody body, int index, CilInstruction instruction)
    {
        switch (instruction.OpCode.OperandType)
        {
            case CilOperandType.InlineField when instruction.Operand is IFieldDescriptor field:
            {
                instruction.Operand = field.ImportWith(importer);
                break;
            }

            case CilOperandType.InlineMethod when instruction.Operand is IMethodDescriptor methodDescriptor:
            {
                var newMethodDescriptor = methodDescriptor.ImportWith(importer);
                if (!SignatureComparer.Default.Equals(newMethodDescriptor, methodDescriptor))
                {
                    if (instruction.OpCode.Code == CilCode.Callvirt && !newMethodDescriptor.Signature!.HasThis)
                    {
                        instruction.OpCode = CilOpCodes.Call;
                    }

                    if (instruction.OpCode.Code == CilCode.Newobj && newMethodDescriptor.Name != ".ctor")
                    {
                        instruction.OpCode = CilOpCodes.Call;
                    }

                    instruction.Operand = newMethodDescriptor;
                }

                break;
            }

            case CilOperandType.InlineSig when instruction.Operand is StandAloneSignature standAlone:
            {
                instruction.Operand = new StandAloneSignature(standAlone.Signature switch
                {
                    MethodSignature signature => signature.ImportWith(importer),
                    GenericInstanceMethodSignature signature => signature.ImportWith(importer),
                    _ => throw new ArgumentOutOfRangeException(),
                });

                break;
            }

            case CilOperandType.InlineType when instruction.Operand is ITypeDefOrRef typeDescriptor:
            {
                instruction.Operand = typeDescriptor.ImportWith(importer);
                break;
            }

            case CilOperandType.InlineTok:
            {
                if (instruction.Operand is IImportable importable)
                {
                    instruction.Operand = importable.ImportWith(importer);
                }

                break;
            }
        }
    }
}
