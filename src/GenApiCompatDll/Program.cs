using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE.DotNet.Metadata.Tables;
using NuGet.Frameworks;
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
        => (path: pkgPath, name: Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pkgPath))!)),
            fwks: Directory.EnumerateDirectories(Path.Combine(pkgPath, "lib"))
                .Select(libPath => (fwk: NuGetFramework.ParseFolder(Path.GetFileName(libPath)),
                    files: Directory.GetFiles(libPath, "*.dll")))
                .ToDictionary(t => t.fwk, t => t.files)))
    .ToDictionary(t => t.name, t => (t.path, t.fwks));

// load available shims (by name)
var shimsByName = Directory.EnumerateFiles(shimsDir, "*.dll", new EnumerationOptions() { RecurseSubdirectories = true })
    .Where(f => Path.GetDirectoryName(f) != shimsDir)
    .Select(f => (path: f, name: Path.GetFileName(f), fwk: NuGetFramework.ParseFolder(Path.GetFileName(Path.GetDirectoryName(f))!)))
    .GroupBy(t => t.name)
    .ToDictionary(
        g => g.Key,
        g => g.ToDictionary(t => t.fwk, t => t.path));


// load our tfm list
var packageTfms = reducer.ReduceEquivalent(
    packages
        .SelectMany(t => t.Value.fwks)
        .Select(t => t.Key)
    ).ToArray();
var tfms = reducer.ReduceEquivalent(
        File.ReadAllLines(tfmsFilePath)
            .Select(NuGetFramework.ParseFolder)
            .Concat(packageTfms)
    )
    .Order(precSorter)
    .ToArray();

// eagerly-load all input assemblies, and collect set of type exports
// file path -> module definition
var assemblies = new Dictionary<string, ModuleDefinition>();
// type full name -> TypeExport instance
var exports = new Dictionary<string, TypeExport>();
var assemblyRefsByName = new Dictionary<string, AssemblyReference>();
var didReportError = false;
foreach (var (pkgName, (_, fwks)) in packages)
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
        new(1,0,0,0))
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
        var pkgFwks = packages[export.FromPackage].fwks;
        if (reducer.GetNearest(tfm, pkgFwks.Keys) is not null)
        {
            // valuetuple is a bit special...
            if (export.FromPackage.Equals("system.valuetuple", StringComparison.OrdinalIgnoreCase)
                && tfm is { Framework: ".NETFramework" } && tfm.Version >= new Version(4,7,1))
            {
                // on .NET Framework 4.7.1, System.ValueTuple was moved into mscorlib
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
    foreach (var (_, (_, fwks)) in packages)
    {
        var best = reducer.GetNearest(framework, fwks.Keys);
        if (best is null) continue; // no match, shouldn't be included
        var pkgFiles = fwks[best].AsEnumerable();

        // if we want to use shims instead, remap all of the files to use the shims
        if (useShim)
        {
            pkgFiles = pkgFiles
                .Select(f => Path.GetFileName(f))
                .Select(n => shimsByName[n])
                .Select(dict => dict[reducer.GetNearest(best, dict.Keys)!]);
        }

        // then add them to the dll list
        dlls = dlls.Concat(pkgFiles);
    }

    return string.Join(",", dlls);
}

sealed record TypeExport(string FromPackage, string FromFile, Utf8String? Namespace, Utf8String Name, ImmutableArray<TypeExport> Nested);
