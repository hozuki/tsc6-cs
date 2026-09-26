namespace TypeScript.Compiler.Tools;

/// <summary>
/// Generates the C# tables and node model that mirror the TypeScript compiler's own type declarations.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// dotnet run --project tools/Gen -- [--ts-root &lt;TypeScript repo&gt;] [--out &lt;dir&gt;] [--check] [--stats]
/// </code>
/// The type model is extracted by <c>tools/Gen/extract.cjs</c>, which loads the TypeScript compiler from the
/// checkout's own <c>lib/typescript.js</c>. <c>--check</c> regenerates in memory and reports drift instead of
/// writing, so it can be wired into CI (and into the unit tests) to catch a stale generated file.
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

        TsModel model;
        try
        {
            // The extracted JSON is a build artifact: it is derived from the checkout on every run, so it is
            // written under artifacts/ rather than committed (unlike the C# it produces).
            model = TsModel.Extract(
                repoRoot,
                tsRoot,
                Path.Combine(repoRoot, "artifacts", "ts-model.json"),
                Generator.TypeModelSources);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            Console.Error.WriteLine($"gen: {ex.Message}");
            return 2;
        }

        if (stats)
        {
            return PrintStats(model);
        }

        List<GeneratedFile> files;
        try
        {
            files = Generator.Generate(model, outRoot, warn: m => Console.Error.WriteLine($"gen: {m}"));
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
            File.WriteAllText(targetPath, file.Content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"gen: wrote {file.RelativePath}");
        }

        return hadDrift ? 1 : 0;
    }

    /// <summary>Reports what the type model contains — a diagnostic aid when the generator misbehaves.</summary>
    private static int PrintStats(TsModel model)
    {
        Console.WriteLine();
        Console.WriteLine($"source                 : {model.Source}");
        Console.WriteLine($"enums                  : {model.Enums.Count}");
        Console.WriteLine($"interfaces             : {model.Interfaces.Count}");
        Console.WriteLine($"  concrete nodes       : {model.Interfaces.Count(static i => i.HasConcreteKind)}");
        Console.WriteLine($"  members              : {model.Interfaces.Sum(static i => i.Members.Count)}");
        Console.WriteLine($"type aliases           : {model.Aliases.Count}");

        Console.WriteLine();
        Console.WriteLine("largest interfaces:");
        foreach (var iface in model.Interfaces.OrderByDescending(static i => i.Members.Count).Take(6))
        {
            Console.WriteLine($"  {iface.Name,-40} {iface.Members.Count,3} members");
        }

        Console.WriteLine();
        Console.WriteLine("member type shapes:");
        var shapes = model.Interfaces
            .SelectMany(static i => i.Members)
            .GroupBy(static m => ClassifyShape(m.Type))
            .OrderByDescending(static g => g.Count());
        foreach (var group in shapes) Console.WriteLine($"  {group.Key,-22} {group.Count(),5}");

        var typeModel = new TsTypeModel(model.Interfaces, model.Aliases);
        var classes = model.Interfaces.Count(i => typeModel.RoleOf(i.Name) == TypeRole.Class);
        var mixins = model.Interfaces.Count(i => typeModel.RoleOf(i.Name) == TypeRole.Interface);

        Console.WriteLine();
        Console.WriteLine("type model (D1: chain steps -> class, mixins -> interface):");
        Console.WriteLine($"  classes              : {classes}");
        Console.WriteLine($"  interfaces (mixins)  : {mixins}");
        Console.WriteLine($"  CallExpression chain : {string.Join(" -> ", typeModel.ClassChainOf("CallExpression").Take(9))}");
        Console.WriteLine($"  mixins of Identifier : {string.Join(", ", typeModel.InterfaceClosureOf("Identifier", transitive: true))}");

        Console.WriteLine();
        Console.WriteLine("enum kinds:");
        Console.WriteLine($"  string-valued        : {model.Enums.Count(static e => e.IsStringValued)}");
        Console.WriteLine($"  numeric              : {model.Enums.Count(static e => !e.IsStringValued)}");

        return 0;
    }

    private static string ClassifyShape(string type)
    {
        var t = type.Trim();
        if (t.Contains("=>", StringComparison.Ordinal)) return "function";
        if (t.Contains("keyof ", StringComparison.Ordinal)) return "keyof";
        if (t.Contains('{')) return "object-literal";
        if (t.StartsWith("readonly ", StringComparison.Ordinal) && t.EndsWith("[]", StringComparison.Ordinal)) return "readonly-array";
        if (t.EndsWith("[]", StringComparison.Ordinal)) return "array";
        if (t.Contains('|')) return "union";
        if (t.Contains('&')) return "intersection";
        if (t.Contains('<')) return "generic";
        return "simple";
    }

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
        Console.WriteLine("  --stats    report the extracted type model without generating");
    }
}
