using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Cloning;
using AsmResolver.DotNet.Serialized;
using AsmResolver.DotNet.Signatures;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Metadata;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.DotNet.StrongName;
using Postprocess;

if (args is not [
    var outputBackports, var outputRefDir,
    var snkPath,
    var backportsPath, var ilhelpersPath, ..var dotnetShimAssemblyPaths
    ])
{
    Console.Error.WriteLine("Assemblies not provided.");
    Console.Error.WriteLine("Syntax: <output dll> <output ref dir> <snk directory> <MonoMod.Backports.dll> <MonoMod.ILHelpers.dll> <...oob package assemblies...>");
    return 1;
}

using var fileservice = new ByteArrayFileService();
var listener = ThrowErrorListener.Instance;

// load baseline assemblies
var readerParams = new ModuleReaderParameters(fileservice)
{
    PEReaderParameters = new(listener)
};
var (context, backportsImage, backportsModule) = LoadContextForRootModule(backportsPath, readerParams);
var backportsResolver = (AssemblyResolverBase)context.AssemblyResolver;
var ilhelpersModule = LoadModuleInContext(context, ilhelpersPath);

var peImageBuilder = new ManagedPEImageBuilder(
    new VersionedMetadataDotnetDirectoryFactory(backportsImage.DotNetDirectory!.Metadata!.VersionString),
    listener);

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

_ = Directory.CreateDirectory(outputRefDir);

// Now, we have some work to do for shims. We want to check if there is an equivalent type to the shims
// defined or exported from Backports, and rewrite a new shim forwarding to Backports as appropriate.
foreach (var shimPath in dotnetShimAssemblyPaths)
{
    var bclShim = ModuleDefinition.FromFile(shimPath, readerParams);

    // set up the new shim module
    var backportsShim = new ModuleDefinition(bclShim.Name, (AssemblyReference)backportsModule.CorLibTypeFactory.CorLibScope)
    {

    };
    var backportsShimAssembly = new AssemblyDefinition(bclShim.Assembly!.Name,
        new(bclShim.Assembly!.Version.Major, 9999, 9999, 9999))
    {
        Modules = { backportsShim },
        HashAlgorithm = bclShim.Assembly!.HashAlgorithm,
        PublicKey = bclShim.Assembly!.PublicKey,
        HasPublicKey = bclShim.Assembly!.HasPublicKey,
    };

    var backportsReference = new AssemblyReference(backportsModule.Assembly!)
        .ImportWith(backportsShim.DefaultImporter);

    // copy attributes
    foreach (var backportsAttr in backportsModule.Assembly!.CustomAttributes)
    {
        var caSig = new CustomAttributeSignature();

        foreach (var arg in backportsAttr.Signature!.FixedArguments)
        {
            caSig.FixedArguments.Add(new(
                backportsShim.DefaultImporter.ImportTypeSignature(arg.ArgumentType),
                arg.Elements));
        }
        foreach (var arg in backportsAttr.Signature!.NamedArguments)
        {
            caSig.NamedArguments.Add(new(
                arg.MemberType,
                arg.MemberName,
                backportsShim.DefaultImporter.ImportTypeSignature(arg.ArgumentType),
                new(
                    backportsShim.DefaultImporter.ImportTypeSignature(arg.Argument.ArgumentType),
                    arg.Argument.Elements)));
        }

        var ca = new CustomAttribute(
            (ICustomAttributeType?)backportsShim.DefaultImporter.ImportMethodOrNull(backportsAttr.Constructor),
            caSig);
        
        backportsShimAssembly.CustomAttributes.Add(ca);
    }

    // go through all public types, make sure they exist in the shim, and generate the forwarder
    foreach (var type in bclShim.TopLevelTypes)
    {
        void ExportType(TypeDefinition type, bool nested)
        {
            if (!type.IsPublic) return;

            // note: we don't do any validation here, because we'll invoke apicompat for that logic after generating everything
            backportsShim.ExportedTypes.Add(new ExportedType(backportsReference, type.Namespace, type.Name)
            {
                Attributes = nested ? 0 : TypeAttributes.Forwarder,
            });

            foreach (var nestedType in type.NestedTypes)
            {
                ExportType(nestedType, true);
            }
        }

        ExportType(type, false);
    }

    // write out the resultant shim assembly
    var newShimPath = Path.Combine(outputRefDir, Path.GetFileName(shimPath));
    backportsShim.Write(newShimPath, peImageBuilder);

    // then finalize the strong name as needed
    if (backportsShimAssembly.HasPublicKey)
    {
        var name = Convert.ToHexString(backportsShimAssembly.GetPublicKeyToken()!);
        var keyPath = Path.Combine(snkPath, name + ".snk");
        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"Cannot finalize strong-name for {newShimPath}!");
            Console.Error.WriteLine($"    Requires SNK file for public key token {keyPath}.");
        }
        else
        {
            var snk = StrongNamePrivateKey.FromFile(keyPath);
            var signer = new StrongNameSigner(snk);
            using var assemblyFs = File.Open(newShimPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            signer.SignImage(assemblyFs, backportsShimAssembly.HashAlgorithm);
        }
    }
}

return 0;

static (RuntimeContext context, PEImage image, ModuleDefinition module) LoadContextForRootModule(string path, ModuleReaderParameters readerParams)
{
    var image = PEImage.FromFile(path, readerParams.PEReaderParameters);
    var module = ModuleDefinition.FromImage(image, readerParams);
    var context = module.RuntimeContext;

    var asmRef = new AssemblyReference(module.Assembly!);
    if (context.AssemblyResolver.Resolve(asmRef) is { ManifestModule: not null } asm)
    {
        return (context, image, asm.ManifestModule);
    }

    ((AssemblyResolverBase)context.AssemblyResolver).AddToCache(asmRef, module.Assembly!);
    return (context, image, module);
}
static ModuleDefinition LoadModuleInContext(RuntimeContext context, string path)
{
    var module = ModuleDefinition.FromFile(path, context.DefaultReaderParameters);
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
    var cloner = new MemberCloner(targetModule);
    cloner.AddListener(new InjectTypeClonerListener(targetModule));

    // clone all types
    foreach (var type in sourceModule.TopLevelTypes)
    {
        if (type.IsModuleType) continue;

        //if (new TypeReference(targetModule, targetModule, type.Namespace, type.Name).Resolve() is null)
        {
            cloner.Include(type, recursive: true);
        }
        /*else
        {
            Console.WriteLine($"Skipping type {type.Namespace}.{type.Name} because it already exists");
        }*/
    }

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
