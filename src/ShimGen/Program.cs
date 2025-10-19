using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.Builder;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.DotNet.StrongName;
using AsmResolver.PE.File;
using NuGet.Frameworks;
using ShimGen;
using System.Diagnostics.CodeAnalysis;

if (args is not [
    var outputRefDir,
    var snkPath,
    .. var dotnetOobPackagePaths
    ])
{
    Console.Error.WriteLine("Assemblies not provided.");
    Console.Error.WriteLine("Syntax: <output ref dir> <snk directory> <...oob package paths...>");
    Console.Error.WriteLine("Arguments provided: ");
    foreach (var arg in args)
    {
        Console.Error.WriteLine($"- {arg}");
    }
    return 1;
}

using var fileservice = new ByteArrayFileService();
var listener = ThrowErrorListener.Instance;

var readerParams = new ModuleReaderParameters(fileservice)
{
    PEReaderParameters = new(listener)
};

var peImageBuilder = new ManagedPEImageBuilder(listener);

_ = Directory.CreateDirectory(outputRefDir);

// first pass: compute TFMs and collect list for each package
var pkgList = dotnetOobPackagePaths
    .Select(pkgPath
        => (pkgPath, Directory.EnumerateDirectories(Path.Combine(pkgPath, "lib"))
            .Select(libPath => (libPath, fwk: NuGetFramework.ParseFolder(Path.GetFileName(libPath)),
                dlls: Directory.EnumerateFiles(libPath, "*.dll").Concat(Directory.EnumerateFiles(libPath, "_._")).ToArray()))
            .ToArray()))
    .ToArray();

var fwReducer = new FrameworkReducer();

// now, we want to collect each distinct set of input libs by tfm
// We need to NOT generate a shim anytime the OOB lib doesn't exist (which happens because the lib exists in the BCL), but otherwise want to generate it whenever
// there is a package version for it. So, we need to:
//  - Collect all TFMs that each lib provides for
//  - Consider each UNIQUE set of libs by tfm
//  - For each UNIQUE set, reduce TFMs down to minimum of kind

// first, arrange the lookups in a reasonable manner
// pkgPath -> framework -> dllPaths
var packageLayout = new Dictionary<string, Dictionary<NuGetFramework, List<(string dllPath, string dllName)>>>();
var dllsByDllName = new Dictionary<string, Dictionary<NuGetFramework, List<string>>>();
foreach (var (pkgPath, libPath, framework, dllPath) in pkgList
    .SelectMany(ta
        => ta.Item2.SelectMany(tb
            => tb.dlls.Select(dll => (ta.pkgPath, tb.libPath, tb.fwk, dll)))))
{
    /*if (framework is
        {
            Framework: ".NETStandard",
            Version.Major: < 2
        })
    {
        // this is .NET Standard < 2.0; we do not want to try to support it. Skip.
        continue;
    }*/

    if (!packageLayout.TryGetValue(pkgPath, out var fwDict))
    {
        packageLayout.Add(pkgPath, fwDict = new());
    }

    if (!fwDict.TryGetValue(framework, out var pathList))
    {
        fwDict.Add(framework, pathList = new());
    }

    var dllName = Path.GetFileName(dllPath);
    if (dllName == "_._")
    {
        continue;
    }

    pathList.Add((dllPath, dllName));

    if (!dllsByDllName.TryGetValue(dllName, out var dllPathList))
    {
        dllsByDllName.Add(dllName, dllPathList = new());
    }

    if (!dllPathList.TryGetValue(framework, out var dllPathSet))
    {
        dllPathList.Add(framework, dllPathSet = new());
    }

    dllPathSet.Add(dllPath);
}

// collect the list of ALL target frameworks that we might care about
var targetTfms = fwReducer
    .ReduceEquivalent(packageLayout.Values.SelectMany(v => v.Keys))
    .Where(fwk => fwk.Framework is ".NETFramework" or ".NETStandard" or ".NETCoreApp") // filter to just the standard Frameworks, because AsmResolver can't handle all the wacko ones
    .Where(fwk => fwk is not { Framework: ".NETStandard", Version.Major: < 2 })
    .ToArray();

// then build up a mapping of the source files for all of those TFMs
var frameworkGroupLayout = new Dictionary<NuGetFramework, List<(string dllName, string assemblyFullName)>>();
foreach (var tfm in targetTfms)
{
    if (!frameworkGroupLayout.TryGetValue(tfm, out var list))
    {
        frameworkGroupLayout.Add(tfm, list = new());
    }

    foreach (var (pkgPath, pkgFwks) in packageLayout)
    {
        if (TryGetFrameworkKey(pkgFwks, tfm, fwReducer, out var dlls))
        {
            foreach (var (dllPath, _) in dlls)
            {
                var assembly = AssemblyDefinition.FromFile(dllPath);
                // We include FullName here so that if the string name key changes (for some reason) we handle it properly
                list.Add((Path.GetFileName(dllPath), assembly.FullName));
            }
        }
    }

    list.Sort();
}

var precSorter = new FrameworkPrecedenceSorter(DefaultFrameworkNameProvider.Instance, false);

// now we group by unique sets, and pick only the minimial framework for each (of each type)
// this is necesasry because our final package will eventually have a dummy reference for the minimum supported
// for each (particularly net35), but if we just pick the overall minimum (netstandard2.0), net35 would be preferred
// for all .NET Framework targets, even the ones that support NS2.0.
var frameworkAssemblies = frameworkGroupLayout
    .GroupBy(kvp => kvp.Value, SequenceEqualityComparer<(string, string)>.Instance)
    .SelectMany(g =>
        g.Select(kvp => kvp.Key)
            .GroupBy(tfm => tfm.Framework)
            .SelectMany(g => fwReducer.ReduceDownwards(g))
            .Select(fwk => (fwk, g.Key)))
    .OrderBy(t => t.fwk, precSorter)
    .ToArray();

// Now, we have some work to do for shims. We want to check if there is an equivalent type to the shims
// defined or exported from Backports, and rewrite a new shim forwarding to Backports as appropriate.
var importedSet = new Dictionary<string, ExportedType>();
foreach (var (targetTfm, assemblies) in frameworkAssemblies)
{
    foreach (var (dllName, _) in assemblies)
    {
        importedSet.Clear();

        ModuleDefinition? backportsShim = null;
        AssemblyDefinition? backportsShimAssembly = null;
        AssemblyReference? backportsReference = null;

        var bclShimPath = GetFrameworkKey(dllsByDllName[dllName], targetTfm, fwReducer).First();

        var bclShim = ModuleDefinition.FromFile(bclShimPath, readerParams);

        var bcl = KnownCorLibs.FromRuntimeInfo(
            DotNetRuntimeInfo.Parse(
                targetTfm.GetDotNetFrameworkName(DefaultFrameworkNameProvider.Instance)));

        // set up the new shim module
        backportsShim = new ModuleDefinition(bclShim.Name, bcl)
        {
            RuntimeVersion = bclShim.RuntimeVersion
        };
        backportsShimAssembly = new AssemblyDefinition(bclShim.Assembly!.Name,
            new(bclShim.Assembly!.Version.Major, 9999, 9999, 9999))
        {
            Modules = { backportsShim },
            HashAlgorithm = bclShim.Assembly!.HashAlgorithm,
            PublicKey = bclShim.Assembly!.PublicKey,
            HasPublicKey = bclShim.Assembly!.HasPublicKey,
        };

        backportsReference =
            new AssemblyReference("MonoMod.Backports", new(1, 0, 0, 0))
            .ImportWith(backportsShim.DefaultImporter);

        foreach (var file in dllsByDllName[dllName].Values.SelectMany(x => x))
        {
            bclShim = ModuleDefinition.FromFile(file, readerParams);

            // go through all public types, make sure they exist in the shim, and generate the forwarder
            foreach (var type in bclShim.TopLevelTypes)
            {
                void ExportType(TypeDefinition type, bool nested, IImplementation impl)
                {
                    if (!type.IsPublic) return;

                    if (!importedSet.TryGetValue(type.FullName, out var expType))
                    {
                        // note: we don't do any validation here, because we'll invoke apicompat for that logic after generating everything
                        backportsShim.ExportedTypes.Add(expType = new ExportedType(impl, type.Namespace, type.Name)
                        {
                            Attributes = nested ? 0 : TypeAttributes.Forwarder,
                        });
                        importedSet.Add(type.FullName, expType);
                    }

                    foreach (var nestedType in type.NestedTypes)
                    {
                        ExportType(nestedType, true, expType);
                    }
                }

                ExportType(type, false, backportsReference);
            }
        }

        if (backportsShim is null || peImageBuilder is null || backportsShimAssembly is null)
        {
            Console.Error.WriteLine($"ShimGen : error : No assemblies were found for TFM {targetTfm} with name {dllName}!");
            continue;
        }

        // write out the resultant shim assembly
        var newShimFolder = Path.Combine(outputRefDir, targetTfm.GetShortFolderName());
        Directory.CreateDirectory(newShimFolder);
        var newShimPath = Path.Combine(newShimFolder, backportsShim.Name!);
        backportsShim.Write(newShimPath, peImageBuilder);

        // then finalize the strong name as needed
        if (backportsShimAssembly.HasPublicKey)
        {
            var delaySignedKeys = new[]
            {
                "B03F5F7F11D50A3A", // MSFT
            };

            var name = Convert.ToHexString(backportsShimAssembly.GetPublicKeyToken()!);
            if (delaySignedKeys.Contains(name))
            {
                var pefile = PEFile.FromFile(newShimPath);
                var image = PEImage.FromFile(pefile);
                image.DotNetDirectory!.Flags |= AsmResolver.PE.DotNet.DotNetDirectoryFlags.StrongNameSigned;
                image.ToPEFile(new ManagedPEFileBuilder(listener)).Write(newShimPath);
            }
            else
            {
                var keyPath = Path.Combine(snkPath, name + ".snk");
                if (!File.Exists(keyPath))
                {
                    Console.Error.WriteLine($"ShimGen : error : Missing SNK file for key {name} (for {backportsShim.Name})");
                    continue;
                }
                var snk = StrongNamePrivateKey.FromFile(keyPath);
                var signer = new StrongNameSigner(snk);
                using var assemblyFs = File.Open(newShimPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                signer.SignImage(assemblyFs, backportsShimAssembly.HashAlgorithm);
            }
        }
    }

    Console.WriteLine("tfm:" + targetTfm.GetShortFolderName());
}

return 0;

static bool TryGetFrameworkKey<T>(Dictionary<NuGetFramework, T> dict, NuGetFramework key, FrameworkReducer reducer, [MaybeNullWhen(false)] out T value)
{
    var best = reducer.GetNearest(key, dict.Keys);
    if (best is null)
    {
        value = default;
        return false;
    }

    return dict.TryGetValue(best, out value);
}


static T GetFrameworkKey<T>(Dictionary<NuGetFramework, T> dict, NuGetFramework key, FrameworkReducer reducer)
{
    var best = reducer.GetNearest(key, dict.Keys);
    return dict[best!];
}

