namespace CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

public abstract class ElementMapper<T>
{
    public T? Left { get; set; }
    public T? Right { get; set; }

    public T? this[ElementSide side] => side switch
    {
        ElementSide.Left => Left,
        ElementSide.Right => Right,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };

    public virtual void Add(T value, ElementSide side)
    {
        if (this[side] != null)
        {
            throw new InvalidOperationException($"{side} element already set.");
        }

        switch (side)
        {
            case ElementSide.Left:
                Left = value;
                break;
            case ElementSide.Right:
                Right = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
    }

    public void Deconstruct(out T? left, out T? right)
    {
        left = Left;
        right = Right;
    }
}
