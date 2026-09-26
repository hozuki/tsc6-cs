#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/{module.ts,system.ts,esnextAnd2015.ts} + the two helper clusters those
// three read out of `factory/utilities.ts`, `utilities.ts`, `utilitiesPublic.ts` and `core.ts`.
/// <summary>
/// ported from: src/compiler/factory/utilities.ts:565,786-856 + src/compiler/utilities.ts:1295,6530 +
/// src/compiler/utilitiesPublic.ts:1591 + src/compiler/core.ts:1160 — the helpers the three module transformers share
/// that no earlier slice needed: the two external-module-name readers (<c>getLocalNameForExternalImport</c>,
/// <c>getExternalModuleNameLiteral</c>), the module-name-of-a-file reader (<c>tryGetModuleNameFromFile</c>, with the
/// path computation it rests on), and the three predicates/mappers around them.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15 "the reader declares it"): <c>getLocalNameForExternalImport</c>,
/// <c>getExternalModuleNameLiteral</c> and <c>tryGetModuleNameFromFile</c> are members of
/// <c>factory/utilities.ts</c>'s own family — <see cref="FactoryUtilities"/> records the whole cluster in its header as
/// left out — and <c>getExternalModuleNameFromPath</c>/<c>isExportNamespaceAsDefaultDeclaration</c> are
/// <c>utilities.ts</c>'s. The module transformers are their first (and, in the reference, only) readers, so they are
/// stated here beside those transformers; the report names the three files whose home they become when those files
/// next gain a slice.
/// </para>
/// <para>
/// PORT-NOTE (why one file and not three): all three module transformers read the same two name readers
/// (<c>module.ts</c> and <c>system.ts</c> both call <c>getExternalModuleNameLiteral</c> in their
/// <c>createRequireCall</c>, and <c>esnextAnd2015.ts</c> calls it in its own), so one home keeps the port from carrying
/// three copies of a body whose reference is one.
/// </para>
/// </remarks>
internal static class ModuleTransformSupport
{
    /// <summary>ported from: src/compiler/factory/utilities.ts:786-807 — <c>getLocalNameForExternalImport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ImportDeclaration | ExportDeclaration | ImportEqualsDeclaration</c>
    /// and its <c>namespaceDeclaration.name</c> read widens across the three classes the namespace declaration may be;
    /// the port switches over those three (the same treatment the union's other readers give it), and the
    /// <c>SyntaxKind.StringLiteral</c> test is the <c>StringLiteral</c> arm of the port's <c>ModuleExportName</c>.
    /// </remarks>
    public static Identifier? GetLocalNameForExternalImport(NodeFactory factory, Node node, SourceFile sourceFile)
    {
        var namespaceDeclaration = Utilities.GetNamespaceDeclarationNode(node);
        if (
            namespaceDeclaration is not null
            && !TransformationUtilities.IsDefaultImport(node)
            && !IsExportNamespaceAsDefaultDeclaration(node)
        )
        {
            var name = namespaceDeclaration switch
            {
                ImportEqualsDeclaration importEquals => (Node?)importEquals.Name,
                NamespaceImport namespaceImport => namespaceImport.Name,
                NamespaceExport namespaceExport => namespaceExport.Name,
                _ => null,
            };
            if (name is null)
            {
                return null;
            }
            if (NodeTests.IsStringLiteral(name))
            {
                return factory.GetGeneratedNameForNode(node, /*flags*/ null, /*prefix*/ (object?)null, /*suffix*/ null);
            }
            if (Utilities.IsGeneratedIdentifier(name))
            {
                return (Identifier)name;
            }
            var sourceText = Utilities.GetSourceTextOfNodeFromSourceFile(sourceFile, name);
            return factory.CreateIdentifier(Js.IsTruthy(sourceText) ? sourceText : Utilities.IdText(name), null, null);
        }
        if (node.Kind == SyntaxKind.ImportDeclaration && ((ImportDeclaration)node).ImportClause is not null)
        {
            return factory.GetGeneratedNameForNode(node, /*flags*/ null, /*prefix*/ (object?)null, /*suffix*/ null);
        }
        if (node.Kind == SyntaxKind.ExportDeclaration && ((ExportDeclaration)node).ModuleSpecifier is not null)
        {
            return factory.GetGeneratedNameForNode(node, /*flags*/ null, /*prefix*/ (object?)null, /*suffix*/ null);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:814-828 — <c>getExternalModuleNameLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>a || b || c</c> chain over three <c>StringLiteral | undefined</c> values is the port's
    /// <c>??</c> chain — a node is never falsy, so the reference's test is exactly the "is there one" test. The
    /// <c>sourceFile.renamedDependencies</c> read is <see cref="SourceFile.RenamedDependencies"/>, whose port slot is a
    /// <c>Dictionary&lt;string, string&gt;?</c>.
    /// </remarks>
    public static StringLiteral? GetExternalModuleNameLiteral(
        NodeFactory factory,
        Node importNode,
        SourceFile sourceFile,
        IEmitHost host,
        IEmitResolver resolver,
        ICompilerOptions compilerOptions)
    {
        var moduleName = Utilities.GetExternalModuleName(importNode);
        if (moduleName is not null && NodeTests.IsStringLiteral(moduleName))
        {
            return TryGetModuleNameFromDeclaration(importNode, host, factory, resolver, compilerOptions)
                ?? TryRenameExternalModule(factory, (LiteralExpression)moduleName, sourceFile)
                ?? factory.CloneNode((StringLiteral)moduleName);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:833-835 — <c>tryRenameExternalModule</c>.</summary>
    private static StringLiteral? TryRenameExternalModule(NodeFactory factory, LiteralExpression moduleName, SourceFile sourceFile)
    {
        if (sourceFile.RenamedDependencies is { } renamedDependencies
            && renamedDependencies.TryGetValue(moduleName.Text ?? "", out var rename))
        {
            return factory.CreateStringLiteral(rename, null, null);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:839-841 — <c>tryGetModuleNameFromDeclaration</c>.</summary>
    private static StringLiteral? TryGetModuleNameFromDeclaration(
        Node declaration,
        IEmitHost host,
        NodeFactory factory,
        IEmitResolver resolver,
        ICompilerOptions compilerOptions)
        => TryGetModuleNameFromFile(factory, resolver.GetExternalModuleFileFromDeclaration(declaration), host, compilerOptions);

    /// <summary>ported from: src/compiler/factory/utilities.ts:843-856 — <c>tryGetModuleNameFromFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!file</c> guard is the port's <c>is null</c> test and <c>file.moduleName</c>/<c>options.outFile</c>
    /// are read as their own truthiness (<see cref="Js.IsTruthy(string)"/> for the first, the nullable slot for the
    /// second), the same readings the reference's <c>&amp;&amp;</c>/ternary make.
    /// </remarks>
    public static StringLiteral? TryGetModuleNameFromFile(NodeFactory factory, SourceFile? file, IEmitHost host, ICompilerOptions options)
    {
        if (file is null)
        {
            return null;
        }
        if (Js.IsTruthy(file.ModuleName))
        {
            return factory.CreateStringLiteral(file.ModuleName, null, null);
        }
        if (!file.IsDeclarationFile && Js.IsTruthy(options.OutFile))
        {
            return factory.CreateStringLiteral(GetExternalModuleNameFromPath(host, file.FileName!), null, null);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6530-6538 — <c>getExternalModuleNameFromPath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>const getCanonicalFileName = (f: string) =&gt; host.getCanonicalFileName(f)</c> is the port's
    /// cast of the <c>object?</c>-typed host slot (<c>ISourceFileMayBeEmittedHost.GetCanonicalFileName</c>, the
    /// generator's degradation) to <see cref="Core.GetCanonicalFileName"/>, the same cast <c>Emitter.EmitFiles</c> makes.
    /// </remarks>
    public static string GetExternalModuleNameFromPath(IEmitHost host, string fileName, string? referencePath = null)
    {
        Core.GetCanonicalFileName getCanonicalFileName = (Core.GetCanonicalFileName)host.GetCanonicalFileName!;
        var currentDirectory = ((IScriptReferenceHost)host).GetCurrentDirectory();
        var dir = Paths.ToPath(
            referencePath is not null ? Paths.GetDirectoryPath(referencePath) : host.GetCommonSourceDirectory(),
            currentDirectory,
            getCanonicalFileName);
        var filePath = Paths.GetNormalizedAbsolutePath(fileName, currentDirectory);
        var relativePath = Paths.GetRelativePathToDirectoryOrUrl(dir, filePath, dir, getCanonicalFileName, /*isAbsolutePathAnUrl*/ false);
        var extensionless = Utilities.RemoveFileExtension(relativePath);
        return referencePath is not null ? Paths.EnsurePathIsNonModuleName(extensionless) : extensionless;
    }

    /// <summary>ported from: src/compiler/utilities.ts:1295-1297 — <c>isExportNamespaceAsDefaultDeclaration</c>.</summary>
    public static bool IsExportNamespaceAsDefaultDeclaration(Node node)
        => NodeTests.IsExportDeclaration(node)
            && ((ExportDeclaration)node).ExportClause is not null
            && NodeTests.IsNamespaceExport(((ExportDeclaration)node).ExportClause!)
            && Utilities.ModuleExportNameIsDefault(((NamespaceExport)((ExportDeclaration)node).ExportClause!).Name!);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1591-1596 — <c>isFileLevelReservedGeneratedIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.emitNode.autoGenerate.flags</c>; the port's <c>EmitNodes.GetIdentifierAutoGenerate</c>
    /// is that read and the <c>!</c> is the reference's own non-null assumption (the predicate is only called on
    /// identifiers the checker already proved generated, <c>module.ts:2435</c>).
    /// </remarks>
    public static bool IsFileLevelReservedGeneratedIdentifier(Node node)
    {
        var flags = EmitNodes.GetIdentifierAutoGenerate(node)!.Flags;
        return (flags & GeneratedIdentifierFlags.FileLevel) != 0
            && (flags & GeneratedIdentifierFlags.Optimistic) != 0
            && (flags & GeneratedIdentifierFlags.ReservedInNestedScopes) != 0;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8006-8017 — <c>isDeclarationNameOfEnumOrNamespace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>utilities.ts</c>'s predicate; <c>module.ts</c> is its only reader in the reference
    /// (<c>module.ts:1165</c>, in <c>visitPreOrPostfixUnaryExpression</c>), so it is stated here under the same
    /// "the reader declares it" rule the file header records. TS's <c>if (parseNode)</c> is the port's
    /// <c>is not null</c> test and its <c>parseNode.parent.kind</c> switch reads the node's own parent.
    /// </remarks>
    public static bool IsDeclarationNameOfEnumOrNamespace(Identifier node)
    {
        var parseNode = Utilities.GetParseTreeNode(node);
        if (parseNode is not null)
        {
            switch (parseNode.Parent!.Kind)
            {
                case SyntaxKind.EnumDeclaration:
                    return ReferenceEquals(parseNode, ((EnumDeclaration)parseNode.Parent).Name);
                case SyntaxKind.ModuleDeclaration:
                    return ReferenceEquals(parseNode, ((ModuleDeclaration)parseNode.Parent).Name);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10027-10029 — <c>hasJSFileExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>utilities.ts</c>'s predicate, stated here for the same reason as
    /// <see cref="IsDeclarationNameOfEnumOrNamespace"/>; <c>module.ts</c>'s <c>shouldEmitUnderscoreUnderscoreESModule</c>
    /// (<c>module.ts:261</c>) is its only reader. The body is the port's own <c>hasTSFileExtension</c>
    /// (<c>Checker/Utilities.SymbolBand.cs:482</c>) with the JS extension list, which is the reference's
    /// <c>some(supportedJSExtensionsFlat, extension =&gt; fileExtensionIs(fileName, extension))</c>.
    /// </remarks>
    public static bool HasJSFileExtension(string fileName)
        => Utilities.SupportedJSExtensionsFlat.Exists(extension => Paths.FileExtensionIs(fileName, extension));

    /// <summary>ported from: src/compiler/utilities.ts:7983-7990 — <c>rangeContainsRange</c> and its
    /// <c>startEndContainsRange</c> helper.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>utilities.ts</c>'s two range predicates, stated here for the same reason as
    /// <see cref="IsDeclarationNameOfEnumOrNamespace"/>; <c>esnextAnd2015.ts:156</c> is their only reader in the module
    /// cluster (the <c>system.ts</c>/<c>module.ts</c> halves read other members of the same family that have already
    /// landed). TS's <c>r1.pos</c>/<c>r1.end</c> are <see cref="ITextRange"/>'s, whose C# spelling is the interface's own
    /// two properties.
    /// </remarks>
    public static bool RangeContainsRange(ITextRange range1, ITextRange range2)
        => StartEndContainsRange(range1.Pos, range1.End, range2);

    /// <summary>ported from: src/compiler/utilities.ts:7988-7990 — <c>startEndContainsRange</c>.</summary>
    public static bool StartEndContainsRange(int start, int end, ITextRange range)
        => start <= range.Pos && end >= range.End;

    /// <summary>ported from: src/compiler/core.ts:1160-1164 — <c>singleOrMany</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's four overloads collapse to <c>T | T[] | undefined</c>, which the port spells <c>object?</c> —
    /// the same erased answer every visitor slot of this layer takes (<see cref="Utilities.VariableVisitor"/>'s
    /// <c>VisitResult</c>). The reference's <c>array !== undefined</c> is the port's <c>is not null</c>.
    /// </remarks>
    public static object? SingleOrMany<T>(List<T>? array)
        => array is not null && array.Count == 1 ? array[0] : array;
}
