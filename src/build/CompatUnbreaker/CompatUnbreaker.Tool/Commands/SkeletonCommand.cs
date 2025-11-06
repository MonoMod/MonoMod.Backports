using CompatUnbreaker.Tool.SkeletonGeneration;
using ConsoleAppFramework;

namespace CompatUnbreaker.Tool.Commands;

internal sealed class SkeletonCommand : BaseShimProjectCommand
{
    [Command("skeleton")]
    public async Task ExecuteAsync([Argument] string projectPath, bool skipBuild = false)
    {
        var assemblyMapper = await LoadAsync(projectPath, skipBuild);

        await SkeletonGenerator.GenerateAsync(assemblyMapper, projectPath);
    }
}
