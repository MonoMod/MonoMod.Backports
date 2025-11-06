using CompatUnbreaker.Tool.Commands;
using ConsoleAppFramework;

namespace CompatUnbreaker.Tool;

internal sealed class Program
{
    public static async Task Main(string[] args)
    {
        var app = ConsoleApp.Create();

        app.Add<CompareCommand>();
        app.Add<SkeletonCommand>();

        await app.RunAsync(args);
    }
}
