using NuGet.Frameworks;
using NuGet.Versioning;

if (args is not [
    var tfmsFilePath,
    .. var dotnetOobPackagePaths
    ])
{
    Console.Error.WriteLine("Assemblies not provided.");
    Console.Error.WriteLine("Syntax: <tfms file> <...oob package paths...>");
    Console.Error.WriteLine("Arguments provided: ");
    foreach (var arg in args)
    {
        Console.Error.WriteLine($"- {arg}");
    }
    return 1;
}

var reducer = new FrameworkReducer();
var precSorter = new FrameworkPrecedenceSorter(DefaultFrameworkNameProvider.Instance, false);

// load packages dict
var packages = dotnetOobPackagePaths
    .Select(pkgPath
        => (name: Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pkgPath))!)),
            version: new NuGetVersion((Path.GetFileName(Path.TrimEndingDirectorySeparator(pkgPath)))),
            fwks: Directory.EnumerateDirectories(Path.Combine(pkgPath, "lib"))
                .Select(libPath => NuGetFramework.ParseFolder(Path.GetFileName(libPath)))
                .ToArray()))
    .GroupBy(t => t.name)
    .Select(g
        => (name: g.Key,
            fwksForVer: g
                .Select(t => (t.version, t.fwks))
                .OrderByDescending(t => t.version)
                .ToArray()));

var tfms = reducer.ReduceEquivalent(
        File.ReadAllLines(tfmsFilePath)
            .Select(NuGetFramework.ParseFolder)
    )
    .Order(precSorter)
    .ToArray();

foreach (var tfm in tfms)
{
    Console.Write($"<ItemGroup Condition=\"'$(TargetFramework)' == '{tfm.GetShortFolderName()}'\">");

    foreach (var (pkgName, fwkByVer) in packages)
    {
        NuGetVersion? resolvedVer = null;
        foreach (var (ver, fwks) in fwkByVer)
        {
            if (resolvedVer is not null && resolvedVer > ver)
            {
                continue;
            }

            if (reducer.GetNearest(tfm, fwks) is not null)
            {
                resolvedVer = ver;
            }
        }

        // no matching version is actually ok, it's fine, we just don't want to output anything for it
        if (resolvedVer is not null)
        {
            Console.Write($"<PackageReference Include=\"{pkgName}\" Version=\"{resolvedVer}\"/>");
        }
    }

    Console.WriteLine("</ItemGroup>");
}

return 0;