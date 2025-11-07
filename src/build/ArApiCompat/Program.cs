using ArApiCompat;
using ArApiCompat.ApiCompatibility.Suppressions;
using System.Xml.Linq;

Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

if (args is not [{ } suppressionFile, { } leftAssemblyFile, { } leftRefPathFile, { } rightAssemblyFile, { } rightRefPathFile, ..var rest])
{
    Console.Error.WriteLine("Usage: ArApiCompat <suppression file> <left assembly> <left reference path file> <right assembly> <right reference path file> [--write-suppressions]");
    return 1;
}

var writeSuppression = rest is ["--write-suppressions", ..];

var result = ComparisonResult.Execute(
    [
        new(
            Path.GetFileName(Path.GetDirectoryName(leftAssemblyFile)) ?? leftAssemblyFile,
            leftAssemblyFile,
            File.ReadAllLines(leftRefPathFile),
            Path.GetFileName(Path.GetDirectoryName(rightAssemblyFile)) ?? leftAssemblyFile,
            rightAssemblyFile,
            File.ReadAllLines(rightRefPathFile)
            )
    ],
    File.Exists(suppressionFile)
    ? SuppressionFile.Deserialize(XDocument.Load(suppressionFile))
    : null);

if (writeSuppression)
{
    var suppressions = result.GetSuppressionFile();
    var doc = suppressions.Serialize();
    doc.Save(suppressionFile, SaveOptions.OmitDuplicateNamespaces);

    return 0;
}
else
{
    var anyError = false;
    for (var i = 0; i < result.JobCount; i++)
    {
        var job = result.Jobs[i];
        var differences = result.GetDifferences(i);
        if (differences.Count > 0)
        {
            anyError = true;
            Console.WriteLine($"error : Compatability errors between '{job.LeftName}' and '{job.RightName}':");

            foreach (var difference in differences)
            {
                Console.WriteLine($"error {difference}");
            }

            Console.WriteLine("---");
        }
        else
        {
            // no differences, don't report anything
        }
    }

    if (result.HasUnusedSuppressions)
    {
        Console.WriteLine($"warning : Suppressions file '{suppressionFile}' has unused suppressions. Regenerate it by passing --write-suppressions to ArApiCompat.");
    }

    return anyError ? 1 : 0;
}
