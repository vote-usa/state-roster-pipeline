using StateBallot.Tools;

var designerArg = (string?)null;
var outArg = (string?)null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--designer" when i + 1 < args.Length:
            designerArg = args[++i];
            break;
        case "--out" when i + 1 < args.Length:
            outArg = args[++i];
            break;
        case "--help" or "-h":
            Console.WriteLine(
                """
                StateBallot.Tools — regen db/schema.sql from VoteProject Vote.designer.cs

                Options:
                  --designer <path>  Vote.designer.cs (default: ../VoteProject.5-main/VoteLibrary/DB/Vote.designer.cs)
                  --out <path>       Output SQL (default: db/schema.sql)
                """);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
    }
}

var repoRoot = FindRepoRoot();
var designerPath = Path.GetFullPath(designerArg
    ?? Path.Combine(Directory.GetParent(repoRoot)!.FullName,
        "VoteProject.5-main", "VoteLibrary", "DB", "Vote.designer.cs"));
var outPath = Path.GetFullPath(outArg ?? Path.Combine(repoRoot, "db", "schema.sql"));

if (!File.Exists(designerPath))
{
    Console.Error.WriteLine(
        $"Vote.designer.cs not found at {designerPath}. Pass --designer or clone VoteProject.5-main next to this repo.");
    return 2;
}

const string regen = "-- Regen: dotnet run --project src/StateBallot.Tools";
var (sql, columnCount) = VoteSchemaExtractor.Generate(File.ReadAllText(designerPath), regen);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, sql);
Console.WriteLine($"Wrote {outPath} ({columnCount} columns across {VoteSchemaExtractor.Tables.Length} tables)");
return 0;

static string FindRepoRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
    {
        if (Directory.Exists(Path.Combine(d.FullName, "src"))
            && Directory.Exists(Path.Combine(d.FullName, "db")))
            return d.FullName;
    }

    return Directory.GetCurrentDirectory();
}
