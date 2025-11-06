using CompatUnbreaker.Tool.ApiCompatibility.AssemblyMapping;

namespace CompatUnbreaker.Tool.ApiCompatibility.Comparing.Rules;

public abstract class BaseRule
{
    public virtual void Run(AssemblyMapper mapper, IList<CompatDifference> differences)
    {
    }

    public virtual void Run(TypeMapper mapper, IList<CompatDifference> differences)
    {
    }

    public virtual void Run(MemberMapper mapper, IList<CompatDifference> differences)
    {
    }
}
