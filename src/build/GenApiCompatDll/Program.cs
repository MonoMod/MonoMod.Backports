using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE.DotNet.Metadata.Tables;
using NuGet.Frameworks;
using NuGet.Versioning;
using System.Collections.Immutable;

if (args is not [
    var outputRefDir,
    var tfmsFilePath,
    var shimsDir,
    .. var dotnetOobPackagePaths
    ])
{
    Console.Error.WriteLine("Assemblies not provided.");
    Console.Error.WriteLine("Syntax: <output ref dir> <tfms file> <shims dir> <...oob package paths...>");
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

var reducer = new FrameworkReducer();
var precSorter = new FrameworkPrecedenceSorter(DefaultFrameworkNameProvider.Instance, false);

// load packages dict
var packages = dotnetOobPackagePaths
    .Select(pkgPath
        => (path: pkgPath,
            name: Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pkgPath))!)),
            version: new NuGet.Versioning.NuGetVersion((Path.GetFileName(Path.TrimEndingDirectorySeparator(pkgPath)))),
            fwks: Directory.EnumerateDirectories(Path.Combine(pkgPath, "lib"))
                .Select(libPath => (fwk: NuGetFramework.ParseFolder(Path.GetFileName(libPath)),
                    files: Directory.GetFiles(libPath, "*.dll")))
                .ToDictionary(t => t.fwk, t => t.files)))
    .GroupBy(t => t.name)
    .Select(g => (name: g.Key,
        fwks: g.Select(t => (t.fwks, t.version))
        .Aggregate(MergeFrameworks)))
    .ToDictionary(t => t.name, t => t.fwks.fwks);

(Dictionary<NuGetFramework, string[]> d, NuGetVersion v) MergeFrameworks((Dictionary<NuGetFramework, string[]> d, NuGetVersion v) a, (Dictionary<NuGetFramework, string[]> d, NuGetVersion v) b)
{
    if (a.v == b.v)
    {
        // unify
        var dict = new Dictionary<NuGetFramework, HashSet<string>>();
        foreach (var (k, v) in a.d)
        {
            dict.Add(k, v.ToHashSet());
        }
        foreach (var (k, v) in b.d)
        {
            if (!dict.TryGetValue(k, out var l))
            {
                dict.Add(k, l = new());
            }
            foreach (var vi in v)
            {
                _ = l.Add(vi);
            }
        }

        return (dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()), a.v);
    }

    if (a.v > b.v)
    {
        // a is newer than b, add frameworks from b not present in a
        foreach (var (k, v) in b.d)
        {
            _ = a.d.TryAdd(k, v);
        }

        return (a.d, a.v);
    }
    else
    {
        // b is newer (or equal) to a, reverse parameters
        return MergeFrameworks(b, a);
    }
}

// load available shims
var allShimDlls = Directory.EnumerateFiles(shimsDir, "*.dll", new EnumerationOptions() { RecurseSubdirectories = true })
    .Where(f => Path.GetDirectoryName(f) != shimsDir)
    .Select(f => (path: f, name: Path.GetFileName(f), fwk: NuGetFramework.ParseFolder(Path.GetFileName(Path.GetDirectoryName(f))!)))
    .ToArray();

var shimsByName = allShimDlls
    .GroupBy(t => t.name)
    .ToDictionary(
        g => g.Key,
        g => g.ToDictionary(t => t.fwk, t => t.path));

var shimsByTfm = allShimDlls
    .GroupBy(t => t.fwk)
    .ToDictionary(
        g => g.Key,
        g => g.ToDictionary(t => t.name, t => t.path));


// load our tfm list
var packageTfmsRaw = reducer.ReduceEquivalent(
    packages
        .SelectMany(t => t.Value)
        .Select(t => t.Key)
    ).ToArray();

var packageTfmsDirect = 
        packageTfmsRaw
        .Where(f => f is not { Framework: ".NETStandard", Version.Major: < 2 }) // make sure we ignore netstandard1.x
        .Where(f => DotNetRuntimeInfo.TryParse(f.GetDotNetFrameworkName(DefaultFrameworkNameProvider.Instance), out var rti)
                && (rti.IsNetFramework || rti.IsNetStandard || rti.IsNetCoreApp));

var backportsTfms = reducer.ReduceEquivalent(
        File.ReadAllLines(tfmsFilePath)
        .Select(NuGetFramework.ParseFolder)
    ).ToArray();
var packageTfmsIndirect = backportsTfms
    .Where(tfm
        => packages.Any(kvp => reducer.GetNearest(tfm, kvp.Value.Keys) is not null));

var packageTfms = reducer.ReduceEquivalent(
    packageTfmsDirect.Concat(packageTfmsIndirect)
    ).ToArray();

var tfms = reducer
    .ReduceEquivalent(backportsTfms.Concat(packageTfmsDirect))
    .Order(precSorter)
    .ToArray();

// eagerly-load all input assemblies, and collect set of type exports
// file path -> module definition
var assemblies = new Dictionary<string, ModuleDefinition>();
// type full name -> TypeExport instance
var exports = new Dictionary<string, TypeExport>();
var assemblyRefsByName = new Dictionary<string, AssemblyReference>();
var didReportError = false;
foreach (var (pkgName, fwks) in packages)
{
    foreach (var file in fwks.SelectMany(kvp => kvp.Value))
    {
        var def = ModuleDefinition.FromFile(file, readerParams);
        assemblies.Add(file, def);

        var fn = Path.GetFileName(file);
        assemblyRefsByName[fn] = new AssemblyReference(def.Assembly!);

        foreach (var type in def.TopLevelTypes)
        {
            void ExportType(TypeDefinition type, ImmutableArray<TypeExport>.Builder? builder)
            {
                if (!type.IsPublic) return;

                if (exports.TryGetValue(type.FullName, out var export))
                {
                    if (export.FromPackage != pkgName)
                    {
                        Console.Error.WriteLine($"GenApiCompatDll : error : Type {type.FullName} is exported from both {export.FromPackage} and {pkgName}");
                        didReportError = true;
                    }
                    else if (export.FromFile != fn)
                    {
                        Console.Error.WriteLine($"GenApiCompatDll : error : Type {type.FullName} is exported from both {export.FromFile} and {fn} in {pkgName}");
                        didReportError = true;
                    }

                    if (export.Nested.Length == 0)
                    {
                        builder?.Add(export);
                        return;
                    }

                    // if there are nested types, fall out so we can get ALL of them
                }

                ImmutableArray<TypeExport> children = [];
                if (type.NestedTypes.Count > 0)
                {
                    var childBuilder = ImmutableArray.CreateBuilder<TypeExport>();
                    foreach (var nested in type.NestedTypes)
                    {
                        ExportType(nested, childBuilder);
                    }
                    if (export is not null)
                    {
                        children.AddRange(export.Nested.Where(e => !childBuilder.Contains(e)));
                    }
                    children = childBuilder.DrainToImmutable();
                }

                export = new TypeExport(pkgName, fn, type.Namespace, type.Name!, children);
                // record the export
                exports[type.FullName] = export;
            }

            ExportType(type, null);
        }
    }
}

if (didReportError)
    return 2;

// flatten the export list correctly
var posDict = new Dictionary<TypeExport, int>();
var exportsList = new List<TypeExport?>();
foreach (var export in exports.Values)
{
    void TrimChildren(TypeExport export)
    {
        foreach (var exp in export.Nested)
        {
            if (posDict.TryGetValue(exp, out var idx))
            {
                exportsList[idx] = null;
            }

            TrimChildren(exp);
        }
    }

    TrimChildren(export);

    posDict[export] = exportsList.Count;
    exportsList.Add(export);
}

for (var i = 0; i < exportsList.Count; i++)
{
    if (exportsList[i] == null)
    {
        exportsList.RemoveAt(i--);
    }
}

var backportsRef = new AssemblyReference("MonoMod.Backports", new(1, 0, 0, 0), false, null);

var generatedAsmFiles = new Dictionary<NuGetFramework, string>();
// now, generate apicompat assemblies appropriately
foreach (var tfm in packageTfms)
{
    // we generate one per TFM
    var bcl = KnownCorLibs.FromRuntimeInfo(
        DotNetRuntimeInfo.Parse(
            tfm.GetDotNetFrameworkName(DefaultFrameworkNameProvider.Instance)));

    var assemblyName = $"Backports.ApiCompat";

    // set up the new shim module
    var module = new ModuleDefinition(assemblyName + ".dll", bcl)
    {
        //RuntimeVersion = bclShim.RuntimeVersion
    };
    var assembly = new AssemblyDefinition(assemblyName,
        new(1, 0, 0, 0))
    {
        Modules = { module },
    };

    // now write out the exports
    foreach (var export in exportsList)
    {
        if (export is null) continue;

        void ExportType(TypeExport export, IImplementation impl, bool nested)
        {
            var exported = new ExportedType(impl, export.Namespace, export.Name)
            {
                Attributes = nested ? 0 : TypeAttributes.Forwarder,
            };
            module.ExportedTypes.Add(exported);

            foreach (var exp in export.Nested)
            {
                ExportType(exp, exported, true);
            }
        }

        IImplementation impl;

        // resolve the implementation for this export
        var pkgFwks = packages[export.FromPackage];
        if (reducer.GetNearest(tfm, pkgFwks.Keys) is not null)
        {
            // valuetuple is a bit special...
            if (export.FromPackage.Equals("system.valuetuple", StringComparison.OrdinalIgnoreCase)
                && tfm is { Framework: ".NETFramework" } && tfm.Version >= new Version(4, 7, 1))
            {
                // on .NET Framework 4.7.1, System.ValueTuple was moved into mscorlib
                impl = (IImplementation)module.CorLibTypeFactory.CorLibScope;
            }
            else
            if (export.FromPackage.Equals("system.runtime.compilerservices.unsafe", StringComparison.OrdinalIgnoreCase)
                && tfm is { Framework: ".NETCoreApp" } && tfm.Version >= new Version(7, 0, 0))
            {
                // on .NET 7, System.Runtime.CompilerServices.Unsafe moved into System.Runtime
                impl = (IImplementation)module.CorLibTypeFactory.CorLibScope;
            }
            else
            {
                // the package provides something compatible with this TFM, reference the FromFile ref
                impl = assemblyRefsByName[export.FromFile];
            }
        }
        else
        {
            impl = backportsRef;
        }

        ExportType(export, (IImplementation)impl.ImportWith(module.DefaultImporter), false);
    }

    // write it out
    var folder = Path.Combine(outputRefDir, tfm.GetShortFolderName());
    Directory.CreateDirectory(folder);
    var newShimPath = Path.Combine(folder, module.Name!);
    module.Write(newShimPath, peImageBuilder);
    generatedAsmFiles[tfm] = newShimPath;
}

// and finally, generate the comparison sets
// note: we don't actually care about intra-tfm comparisons here, because that's handled by apicompat on the main Backports package
// what we care about here is compat between non-shimmed and shimmed
foreach (var framework in packageTfms)
{
    var tfm = framework.GetShortFolderName();
    var fname = generatedAsmFiles[framework];
    var nonShimRefPath = GetReferencePathForTfm(framework, useShim: false);
    var shimRefPath = GetReferencePathForTfm(framework, useShim: true);
    Console.WriteLine($"{tfm}|{fname}|{nonShimRefPath}|{shimRefPath}");
}

return 0;

string GetReferencePathForTfm(NuGetFramework framework, bool useShim)
{
    IEnumerable<string> dlls = [];
    foreach (var (_, fwks) in packages)
    {
        var best = reducer.GetNearest(framework, fwks.Keys);
        if (best is null) continue; // no match, shouldn't be included
        var pkgFiles = fwks[best].AsEnumerable();

        // if we want to use shims instead, remap all of the files to use the shims
        if (useShim)
        {
            var shimTfm = reducer.GetNearest(framework, shimsByTfm.Keys);
            var shimFilesDict = shimsByTfm[shimTfm!];
            pkgFiles = pkgFiles
                .Select(f => Path.GetFileName(f))
                .Select(n => shimFilesDict.TryGetValue(n, out var v) ? v : null)
                .Where(v => v is not null)!;
        }

        // then add them to the dll list
        dlls = dlls.Concat(pkgFiles);
    }

    return string.Join(",", dlls);
}

sealed record TypeExport(string FromPackage, string FromFile, Utf8String? Namespace, Utf8String Name, ImmutableArray<TypeExport> Nested);
