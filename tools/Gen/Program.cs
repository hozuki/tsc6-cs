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
        Console.WriteLine("usage: dotnet run --project tools/Gen -- [--ts-root <path>] [--out <dir>] [--check]");
        Console.WriteLine("  --ts-root  TypeScript checkout to read (default: tools/ts-source.txt)");
        Console.WriteLine($"  --out      output root, relative to the repo (default: {Generator.DefaultOutputRoot})");
        Console.WriteLine("  --check    verify generated files are up to date without writing");
    }
}
