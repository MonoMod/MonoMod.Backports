using System.ComponentModel.DataAnnotations;

#if V1
namespace PlaygroundLibrary;

[Display]
public class TestClass
{
    private readonly int readonlyField;

    [Required]
    public int Property { get => throw null!; set => throw null!; }

    public int GetOnlyProperty { get => throw null!; }
    public int SetOnlyProperty { set => throw null!; }
    public int InitOnlyProperty { get => throw null!; init => throw null!; }

    [Obsolete]
    public event Action<object> Event
    {
        add => throw null!;
        remove => throw null!;
    }
}

public struct Struct
{
    public int a;
    public readonly int b;
}

public record Record(int a, int b);

public readonly record struct StructRecord(int a, int b);

public ref struct RefStruct
{
    public int a;
    public ref int b;
    public ref readonly int c;
    public readonly ref readonly int d;
}
#endif
