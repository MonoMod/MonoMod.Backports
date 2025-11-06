namespace CompatUnbreaker.Tests.TestFiles;

[Test("string")]
[Test(new string[] { "a", "b" })]
[Test(123)]
[Test(typeof(AttributeTests))]
[Test(typeof(Action<,>))]
// [Test(TestEnum.B)] TODO this requires porting CSharpFlagsEnumGenerator
[Test((object?) null)]
[Test((string?) null)]
[Test((string[]?) null)]
[Test((Type?) null)]
public sealed class AttributeTests
{
    public const AttributeTargets Guh = AttributeTargets.Assembly | AttributeTargets.Interface | AttributeTargets.Delegate;

    [Test]
    public int field;

    [Test]
    public extern int Property { get; set; }

    [Test]
    public extern event Action Event;

    [Test]
    public AttributeTests()
    {
    }

    [Test]
    [return: Test]
    public static extern T Method<[Test] T>([Test] T parameter);

    [Test]
    public delegate T Delegate<out T>();

    // [Test]
    // public interface IInterface
    // {
    // }

    [Test]
    public enum TestEnum : byte
    {
        A = 1,
        B = 2
    }

    [Test]
    public struct TestStruct;

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class TestAttribute : Attribute
    {
        public TestAttribute()
        {
        }

        public TestAttribute(string? a)
        {
        }

        public TestAttribute(string[]? a)
        {
        }

        public TestAttribute(object? a)
        {
        }

        public TestAttribute(Type? a)
        {
        }

        public TestAttribute(TestEnum a)
        {
        }
    }
}
