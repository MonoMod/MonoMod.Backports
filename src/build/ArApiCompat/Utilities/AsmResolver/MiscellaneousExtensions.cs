using AsmResolver.PE.DotNet.Cil;

namespace CompatUnbreaker.Tool.Utilities.AsmResolver;

internal static class MiscellaneousExtensions
{
    public static bool Match(this IEnumerable<CilInstruction> instructions, params ReadOnlySpan<Func<CilInstruction, bool>> predicates)
    {
        using var enumerator = instructions.GetEnumerator();

        foreach (var predicate in predicates)
        {
            CilInstruction instruction;

            do
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                instruction = enumerator.Current;
            } while (instruction.OpCode == CilOpCodes.Nop);

            if (!predicate(instruction))
            {
                return false;
            }
        }

        return !enumerator.MoveNext();
    }
}
