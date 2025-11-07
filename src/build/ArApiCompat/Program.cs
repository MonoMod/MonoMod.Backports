using ArApiCompat;
using ArApiCompat.ApiCompatibility.Suppressions;
using System.Xml.Linq;

Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

if (args is not [{ } suppressionFile, { } comparisonsDef, ..var rest])
{
    Console.Error.WriteLine("Usage: ArApiCompat <suppression file> <comparison definition file> [--write-suppressions]");
    return 1;
}

var writeSuppression = rest is ["--write-suppressions", ..];

var comparisonJobs = new List<ComparisonJob>();
var comparisonDefsFile = File.ReadAllLines(comparisonsDef);

var idx = 0;
var lineCount = comparisonDefsFile.Length;

(string? line, int lineNumber) ReadNonEmptyLine()
{
    while (idx < lineCount)
    {
        var currentIndex = idx;
        var l = comparisonDefsFile[idx++].Trim();
        if (l.Length == 0) continue;
        // line numbers are 1-based
        return (l, currentIndex + 1);
    }
    return (null, lineCount > 0 ? lineCount : 1);
}

while (true)
{
    var (leftName, leftNameLine) = ReadNonEmptyLine();
    if (leftName is null) break; // done

    var (leftFile, _) = ReadNonEmptyLine();
    if (leftFile is null)
    {
        Console.Error.WriteLine($"{comparisonsDef}({leftNameLine}): error ARAPI0001: Missing left file for comparison starting with '{leftName}'");
        return 1;
    }

    var (leftRefCountLine, leftRefCountLineNum) = ReadNonEmptyLine();
    if (leftRefCountLine is null || !int.TryParse(leftRefCountLine, out var leftRefCount) || leftRefCount < 0)
    {
        Console.Error.WriteLine($"{comparisonsDef}({leftRefCountLineNum}): error ARAPI0002: Missing or invalid left reference count for comparison starting with '{leftName}'");
        return 1;
    }

    var leftRefPath = new List<string>(leftRefCount);
    for (var  i = 0; i < leftRefCount; i++)
    {
        var (p, _) = ReadNonEmptyLine();
        if (p is null)
        {
            Console.Error.WriteLine($"{comparisonsDef}({lineCount}): error ARAPI0003: Not enough left reference paths for comparison starting with '{leftName}'");
            return 1;
        }
        leftRefPath.Add(p);
    }

    var (rightName, rightNameLine) = ReadNonEmptyLine();
    if (rightName is null)
    {
        Console.Error.WriteLine($"{comparisonsDef}({leftNameLine}): error ARAPI0004: Missing right name for comparison starting with '{leftName}'");
        return 1;
    }

    var (rightFile, _) = ReadNonEmptyLine();
    if (rightFile is null)
    {
        Console.Error.WriteLine($"{comparisonsDef}({rightNameLine}): error ARAPI0005: Missing right file for comparison starting with '{leftName}'");
        return 1;
    }

    var (rightRefCountLine, rightRefCountLineNum) = ReadNonEmptyLine();
    if (rightRefCountLine is null || !int.TryParse(rightRefCountLine, out var rightRefCount) || rightRefCount < 0)
    {
        Console.Error.WriteLine($"{comparisonsDef}({rightRefCountLineNum}): error ARAPI0006: Missing or invalid right reference count for comparison starting with '{leftName}'");
        return 1;
    }

    var rightRefPath = new List<string>(rightRefCount);
    for (var i = 0; i < rightRefCount; i++)
    {
        var (p, _) = ReadNonEmptyLine();
        if (p is null)
        {
            Console.Error.WriteLine($"{comparisonsDef}({lineCount}): error ARAPI0007: Not enough right reference paths for comparison starting with '{leftName}'");
            return 1;
        }
        rightRefPath.Add(p);
    }

    comparisonJobs.Add(new(
        leftName,
        leftFile,
        leftRefPath.ToArray(),
        rightName,
        rightFile,
        rightRefPath.ToArray()));
}

if (comparisonJobs.Count == 0)
{
    Console.Error.WriteLine($"{comparisonsDef}(1): error ARAPI0008: No comparisons found in definitions file.");
    return 1;
}

var result = ComparisonResult.Execute(
    comparisonJobs,
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
