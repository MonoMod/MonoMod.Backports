using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Cloning;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Metadata;
using Postprocess;

if (args is not [
    var outputBackports,
    var backportsPath, var ilhelpersPath,
    .. var referencePath
    ])
{
    Console.Error.WriteLine("Assemblies not provided.");
    Console.Error.WriteLine("Syntax: <output dll> <MonoMod.Backports.dll> <MonoMod.ILHelpers.dll> <reference path item>...");
    Console.Error.WriteLine("Arguments provided: ");
    foreach (var arg in args)
    {
        Console.Error.WriteLine($"- {arg}");
    }
    return 1;
}

using var fileservice = new ByteArrayFileService();
var listener = ThrowErrorListener.Instance;

// load baseline assemblies
var readerParams = new ModuleReaderParameters(fileservice)
{
    PEReaderParameters = new(listener),
};
var resolver = new ReferencePathAssemblyResolver(referencePath, readerParams);
var (context, backportsImage, backportsModule) = LoadContextForRootModule(backportsPath, readerParams, resolver);
readerParams.RuntimeContext = context;
var backportsResolver = (AssemblyResolverBase)context.AssemblyResolver;
var ilhelpersModule = LoadModuleInContext(context, readerParams, ilhelpersPath);

var peImageBuilder = new ManagedPEImageBuilder(listener);

// first, clone in ILHelpers
var cloneResult = CloneModuleIntoModule(ilhelpersModule, backportsModule);
// and write backports back out to disk
backportsModule.Write(outputBackports, peImageBuilder);

// next, try to update the backports PDB
var backportsPdb = Path.ChangeExtension(backportsPath, ".pdb");
var ilhelpersPdb = Path.ChangeExtension(ilhelpersPath, ".pdb");
if (File.Exists(backportsPdb) && File.Exists(ilhelpersPdb))
{
    var backportsPdbMd = MetadataDirectory.FromFile(backportsPdb);
    var ilhelpersPdbMd = MetadataDirectory.FromFile(ilhelpersPdb);

    // TODO: actually manipulate the pdb

    using (var fs = File.Create(Path.ChangeExtension(outputBackports, ".pdb")))
    {
        var sw = new BinaryStreamWriter(fs);
        backportsPdbMd.Write(sw);
    }
}

// and copy the xmldoc if present
var backportsXml = Path.ChangeExtension(backportsPath, ".xml");
if (File.Exists(backportsXml))
{
    File.Copy(backportsXml, Path.ChangeExtension(outputBackports, ".xml"), true);
}

return 0;

static (RuntimeContext context, PEImage image, ModuleDefinition module) LoadContextForRootModule(string path, ModuleReaderParameters readerParams, IAssemblyResolver assemblyResolver)
{
    var image = PEImage.FromFile(path, readerParams.PEReaderParameters);
    var module = ModuleDefinition.FromImage(image, readerParams);

    var context = new RuntimeContext(module.OriginalTargetRuntime, assemblyResolver);
    readerParams.RuntimeContext = context;
    module = ModuleDefinition.FromImage(image, readerParams);

    var asmRef = new AssemblyReference(module.Assembly!);
    if (context.AssemblyResolver.Resolve(asmRef) is { ManifestModule: not null } asm)
    {
        return (context, image, asm.ManifestModule);
    }

    ((AssemblyResolverBase)context.AssemblyResolver).AddToCache(asmRef, module.Assembly!);
    return (context, image, module);
}
static ModuleDefinition LoadModuleInContext(RuntimeContext context, ModuleReaderParameters mrp, string path)
{
    var module = ModuleDefinition.FromFile(path, mrp);
    var asmRef = new AssemblyReference(module.Assembly!);

    if (context.AssemblyResolver.Resolve(asmRef) is { ManifestModule: not null } asm)
    {
        return asm.ManifestModule;
    }

    ((AssemblyResolverBase)context.AssemblyResolver).AddToCache(asmRef, module.Assembly!);
    return module;
}
static MemberCloneResult CloneModuleIntoModule(ModuleDefinition sourceModule, ModuleDefinition targetModule)
{
    var cloner = new MemberCloner(targetModule,
        importerFactory: ctx => new ClonedDuplicateReferenceImporter(targetModule, ctx),
        clonerListener: new InjectTypeClonerListener(targetModule));

    // look at all of the source modules, and get ready to copy all of the forwarders that don't point into the target
    var exports = new List<ExportedType>();
    foreach (var exported in sourceModule.ExportedTypes)
    {
        if (exported.Implementation?.Name != targetModule.Assembly?.Name)
        {
            exports.Add(exported);
        }
    }

    // look at the target module's exports, and remove any pointing into the source
    for (var i = 0; i < targetModule.ExportedTypes.Count; i++)
    {
        var exported = targetModule.ExportedTypes[i];
        if (exported.Implementation?.Name == sourceModule.Assembly?.Name)
        {
            targetModule.ExportedTypes.RemoveAt(i);
            i--;
        }
    }

    // clone all types
    foreach (var type in sourceModule.TopLevelTypes)
    {
        if (type.IsModuleType) continue;

        var existant = new TypeReference(targetModule, targetModule, type.Namespace, type.Name).Resolve();

        if (existant is null)
        {
            cloner.Include(type, recursive: true);
        }
        else
        {
            Console.WriteLine($"Skipping type {type.Namespace}.{type.Name} because it already exists");
        }
    }

    // do the clone
    var cloneResult = cloner.Clone();

    // add the exports
    foreach (var export in exports)
    {
        targetModule.ExportedTypes.Add(new ExportedType(
            targetModule.DefaultImporter.ImportImplementation(export.Implementation),
            export.Namespace,
            export.Name
            ));
    }

    new ClonedReferenceRewriter(cloneResult).RewriteReferences(targetModule);

    return cloneResult;
}
