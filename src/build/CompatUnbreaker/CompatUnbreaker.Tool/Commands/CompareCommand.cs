using CompatUnbreaker.Tool.ApiCompatibility.Comparing;
using ConsoleAppFramework;

namespace CompatUnbreaker.Tool.Commands;

internal sealed class CompareCommand : BaseShimProjectCommand
{
    [Command("compare")]
    public async Task ExecuteAsync([Argument] string projectPath, bool skipBuild = false)
    {
        var assemblyMapper = await LoadAsync(projectPath, skipBuild);

        var apiComparer = new ApiComparer();
        apiComparer.Compare(assemblyMapper);

        foreach (var difference in apiComparer.CompatDifferences)
        {
            Console.WriteLine(difference);
        }
    }
}
