using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// Generates the C# data tables that mirror TS's <c>const enum</c> declarations.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// dotnet run --project tools/Gen -- [--ts-root &lt;TypeScript repo&gt;] [--out &lt;dir&gt;] [--check]
/// </code>
/// <c>--check</c> regenerates in memory and reports drift instead of writing, so it can be wired into CI
/// (and into the unit tests) to catch a stale generated file.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? tsRoot = null;
        var outRoot = Generator.DefaultOutputRoot;
        var check = false;
        var stats = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--ts-root" or "--out":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"gen: '{arg}' requires a value");
                        return 2;
                    }

                    if (arg == "--ts-root") tsRoot = args[++i];
                    else outRoot = args[++i];
                    break;
                case "--check":
                    check = true;
                    break;
                case "--stats":
                    stats = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"gen: unknown argument '{arg}'");
                    PrintUsage();
                    return 2;
            }
        }

        var repoRoot = FindRepoRoot();
        tsRoot ??= ReadDefaultTsRoot(repoRoot);

        if (!Directory.Exists(tsRoot))
        {
            Console.Error.WriteLine($"gen: TypeScript source root not found: '{tsRoot}'");
            Console.Error.WriteLine("     pass --ts-root <path> or fix tools/ts-source.txt");
            return 2;
        }

        if (stats)
        {
            return PrintInterfaceStats(tsRoot);
        }

        List<GeneratedFile> files;
        try
        {
            files = Generator.Generate(tsRoot, outRoot, warn: m => Console.Error.WriteLine($"gen: {m}"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            Console.Error.WriteLine($"gen: {ex.Message}");
            return 2;
        }

        var hadDrift = false;
        foreach (var file in files)
        {
            var targetPath = Path.Combine(repoRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (check)
            {
                var existing = File.Exists(targetPath) ? File.ReadAllText(targetPath) : null;
                if (existing != file.Content)
                {
                    Console.Error.WriteLine($"gen: {file.RelativePath} is out of date");
                    hadDrift = true;
                }
                else
                {
                    Console.WriteLine($"gen: {file.RelativePath} up to date");
                }

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"gen: wrote {file.RelativePath}");
        }

        return hadDrift ? 1 : 0;
    }

    /// <summary>Reports how much of <c>types.ts</c> the interface parser currently understands.</summary>
    private static int PrintInterfaceStats(string tsRoot)
    {
        var path = Path.Combine(tsRoot, "src", "compiler", "types.ts");
        var interfaces = TsInterfaceParser.Parse(File.ReadAllText(path));

        var concrete = interfaces.Where(static i => i.HasConcreteKind).ToList();
        var abstractOnes = interfaces.Where(static i => !i.HasConcreteKind).ToList();

        Console.WriteLine($"interfaces parsed      : {interfaces.Count}");
        Console.WriteLine($"  concrete nodes       : {concrete.Count}");
        Console.WriteLine($"  capability/abstract  : {abstractOnes.Count}");
        Console.WriteLine($"members total          : {interfaces.Sum(static i => i.Members.Count)}");
        Console.WriteLine($"empty interfaces       : {interfaces.Count(static i => i.Members.Count == 0)}");
        Console.WriteLine($"interfaces with bases  : {interfaces.Count(static i => i.Extends.Count != 0)}");
        Console.WriteLine($"interfaces w/ generics : {interfaces.Count(static i => i.TypeParameters.Count != 0)}");

        Console.WriteLine();
        Console.WriteLine("largest interfaces:");
        foreach (var i in interfaces.OrderByDescending(static i => i.Members.Count).Take(10))
        {
            Console.WriteLine($"  {i.Name,-42} {i.Members.Count,3} members, extends [{string.Join(", ", i.Extends)}]");
        }

        Console.WriteLine();
        Console.WriteLine("kinds not covered by a concrete interface (declared as type aliases):");
        var source = File.ReadAllText(path);
        var kindNames = TsEnumParser.Parse(source, "SyntaxKind").Members.Select(static m => m.Name).ToHashSet(StringComparer.Ordinal);
        var fromInterfaces = concrete.Select(static i => i.ConcreteKind!).ToHashSet(StringComparer.Ordinal);
        var missing = kindNames.Where(k => !fromInterfaces.Contains(k)).ToList();
        Console.WriteLine($"  {missing.Count} of {kindNames.Count} SyntaxKind members");
        Console.WriteLine($"  sample: {string.Join(", ", missing.Take(25))}");

        Console.WriteLine();
        Console.WriteLine("member type shapes:");
        var shapes = interfaces
            .SelectMany(static i => i.Members)
            .GroupBy(static m => ClassifyShape(m.Type))
            .OrderByDescending(static g => g.Count());
        foreach (var group in shapes)
        {
            Console.WriteLine($"  {group.Key,-22} {group.Count(),5}");
        }

        Console.WriteLine();
        Console.WriteLine("sample members of each shape:");
        foreach (var group in shapes)
        {
            var sample = group.First();
            Console.WriteLine($"  {group.Key,-22} {sample.Name}: {Truncate(sample.Type, 80)}");
        }

        Console.WriteLine();
        Console.WriteLine("type aliases:");
        var aliases = TsTypeAliasParser.Parse(source);
        Console.WriteLine($"  parsed               : {aliases.Count}");
        var aliasShapes = aliases
            .GroupBy(static a => ClassifyShape(a.Value))
            .OrderByDescending(static g => g.Count());
        foreach (var group in aliasShapes)
        {
            Console.WriteLine($"  {group.Key,-22} {group.Count(),5}   e.g. {group.First().Name} = {Truncate(group.First().Value, 60)}");
        }

        Console.WriteLine();
        Console.WriteLine("type model (D1: chain steps -> class, mixins -> interface):");
        var model = new TsTypeModel(interfaces, aliases);
        var classes = interfaces.Where(i => model.RoleOf(i.Name) == TypeRole.Class).ToList();
        var mixins = interfaces.Where(i => model.RoleOf(i.Name) == TypeRole.Interface).ToList();
        Console.WriteLine($"  classes              : {classes.Count}");
        Console.WriteLine($"  interfaces (mixins)  : {mixins.Count}");
        Console.WriteLine($"  mixes sample         : {string.Join(", ", mixins.Take(14).Select(static i => i.Name))}");

        var withBase = classes.Where(i => model.ClassBaseOf(i.Name) is not (null or "Node")).ToList();
        Console.WriteLine($"  classes w/ base      : {withBase.Count}");
        Console.WriteLine($"  base sample          : {string.Join(", ", withBase.Take(6).Select(i => $"{i.Name}->{model.ClassBaseOf(i.Name)}"))}");

        Console.WriteLine($"  chain depth check    : CallExpression -> {string.Join(" -> ", model.ClassChainOf("CallExpression").Take(9))}");
        Console.WriteLine($"  mixins of Identifier : {string.Join(", ", model.InterfaceClosureOf("Identifier", transitive: true))}");
        Console.WriteLine($"  dual-role classes    : {model.DualRoleClasses.Count} -> {string.Join(", ", model.DualRoleClasses.Take(8))}");
        Console.WriteLine($"  probe                : PrimaryExpression={model.RoleOf("PrimaryExpression")} Token={model.RoleOf("Token")} Identifier.base={model.ClassBaseOf("Identifier")}");

        return 0;
    }

    private static string ClassifyShape(string type)
    {
        var t = type.Trim();
        if (t.Contains("=>", StringComparison.Ordinal)) return "function";
        if (t.Contains("keyof ", StringComparison.Ordinal)) return "keyof";
        if (t.Contains(" extends ", StringComparison.Ordinal)) return "conditional";
        if (t.Contains('{')) return "object-literal";
        if (t.StartsWith("readonly ", StringComparison.Ordinal) && t.EndsWith("[]", StringComparison.Ordinal)) return "readonly-array";
        if (t.EndsWith("[]", StringComparison.Ordinal)) return "array";
        if (t.Contains('|')) return "union";
        if (t.Contains('&')) return "intersection";
        if (t.Contains('<')) return "generic";
        return "simple";
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tsc-cs.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ReadDefaultTsRoot(string repoRoot)
    {
        var configPath = Path.Combine(repoRoot, "tools", "ts-source.txt");
        if (!File.Exists(configPath)) return "";

        foreach (var line in File.ReadAllLines(configPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length != 0 && !trimmed.StartsWith('#')) return trimmed;
        }

        return "";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage: dotnet run --project tools/Gen -- [--ts-root <path>] [--out <dir>] [--check] [--stats]");
        Console.WriteLine("  --ts-root  TypeScript checkout to read (default: tools/ts-source.txt)");
        Console.WriteLine($"  --out      output root, relative to the repo (default: {Generator.DefaultOutputRoot})");
        Console.WriteLine("  --check    verify generated files are up to date without writing");
        Console.WriteLine("  --stats    report how much of types.ts the interface parser understands");
    }
}
