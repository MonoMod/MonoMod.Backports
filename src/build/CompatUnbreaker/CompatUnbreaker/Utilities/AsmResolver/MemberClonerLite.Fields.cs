using AsmResolver.DotNet;
using AsmResolver.DotNet.Cloning;

namespace CompatUnbreaker.Utilities.AsmResolver;

internal readonly partial struct MemberClonerLite
{
    private static readonly FieldRvaCloner s_fieldRvaCloner = new();

    public FieldDefinition CloneField(FieldDefinition field)
    {
        var clonedField = new FieldDefinition(field.Name, field.Attributes, field.Signature?.ImportWith(_importer));

        CloneCustomAttributes(field, clonedField);
        clonedField.ImplementationMap = CloneImplementationMap(field.ImplementationMap);
        clonedField.Constant = CloneConstant(field.Constant);
        clonedField.FieldRva = s_fieldRvaCloner.CloneFieldRvaData(field);
        clonedField.FieldOffset = field.FieldOffset;

        return clonedField;
    }
}
