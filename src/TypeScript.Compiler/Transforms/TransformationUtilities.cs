#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/utilities.ts:1-886 — the whole shared transformer-utilities module
/// <summary>
/// ported from: src/compiler/transformers/utilities.ts:107-886 — the helpers every transformer of the ES2015+ chain
/// shares: the external-module-info collection (<c>collectExternalModuleInfo</c> and its import/export-star
/// predicates), the identifier-name maps, the compound-assignment and simple-expression predicates, the class-member
/// queries the class-field transformers are built on, the decorator queries (<c>AllDecorators</c>), the
/// private/lexical environment carriers, and the module-specifier rewrite.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): TS exports these as module-level functions, constants and two classes of
/// <c>transformers/utilities.ts</c>; C# has no module-level members, so they are the members of the class named after
/// that module and the module's two classes and three interfaces are nested in it (<see cref="IdentifierNameMap{V}"/>,
/// <see cref="IdentifierNameMultiMap{V}"/>, <see cref="ExternalModuleInfo"/>, <see cref="IExternalModuleInfo"/>,
/// <see cref="PrivateEnvironment{TData,TEntry}"/>, <see cref="LexicalEnvironment{TEnvData,TPrivateEnvData,TPrivateEntry}"/>).
/// Consumers that want TS's unqualified spelling can write <c>using static TypeScript.Compiler.TransformationUtilities;</c>.
/// </para>
/// <para>
/// PORT-NOTE (the context face): the reference threads <c>TransformationContext</c> through
/// <c>collectExternalModuleInfo</c>, <c>chainBundle</c> and the destructuring flattener. The generated
/// <see cref="ITransformationContext"/> cannot be implemented by anything in the port — its
/// <c>ICoreTransformationContext.Factory</c> slot is <c>INodeFactory?</c> and no port type implements
/// <c>INodeFactory</c> (docs/architecture.md §15.9), which is the same reason <c>IVisitorTransformationContext</c>
/// (Utilities/Utilities.Visitor.cs:2344) and <c>IEmitHelperFactoryContext</c> exist. <see cref="ITransformationUtilitiesContext"/>
/// is the five members these three files read, its factory slot the concrete <see cref="NodeFactory"/>.
/// </para>
/// <para>
/// PORT-NOTE (members stated here because they are this slice's first reader): <c>getOutputExtension</c>
/// (emitter.ts:540-546, phase 9 of M7), <c>createExternalHelpersImportDeclarationIfNeeded</c> and its two helpers
/// (factory/utilities.ts:697-778 — <c>FactoryUtilities</c> records the omission in its header, its blocking
/// dependency being the module-kind/EmitHost surface this layer now has), <c>getDeclarationName</c>
/// (factory/nodeFactory.ts:6824-6826, one delegate of the <c>getName</c> family §15.9 still lists as unlanded),
/// <c>isDefaultImport</c> (utilities.ts:4441-4443), <c>isEmptyArrayLiteral</c> (utilities.ts:7603-7606, whose only
/// ported copy is private to <c>Checker</c>) and the six <c>is*BindingOrAssignment*</c> predicates
/// (utilitiesPublic.ts:1872-1941, which <c>transformers/destructuring.ts</c> reads). Each is stated at its reference
/// home's name and is listed in the batch report for the milestone that owns its home file.
/// </para>
/// </remarks>
internal static partial class TransformationUtilities
{
    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:106-149 — the module-info helpers, chainBundle
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:107-110 — <c>getOriginalNodeId</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node ? getNodeId(node) : 0</c> tests the result of <c>getOriginalNode</c>, which is a node or
    /// <c>undefined</c> (never a falsy node), so the port's <c>is not null</c> is that test.
    /// </remarks>
    public static int GetOriginalNodeId(Node node)
    {
        var original = Utilities.GetOriginalNode(node);
        return original is not null ? Utilities.GetNodeId(original) : 0;
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:113-122 — <c>ExternalModuleInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares an interface and fills it in with an object literal at the end of
    /// <c>collectExternalModuleInfo</c>; the port follows <see cref="AutoGenerateInfo"/> and states the constructible
    /// class (<see cref="ExternalModuleInfo"/>) and this interface as the pair consumers program against.
    /// </remarks>
    internal interface IExternalModuleInfo
    {
        /// <summary>Imports of other external modules.</summary>
        List<Node> ExternalImports { get; }

        /// <summary>Import of external helpers, if any.</summary>
        Node? ExternalHelpersImportDeclaration { get; }

        /// <summary>File-local export specifiers by name (no reexports).</summary>
        IdentifierNameMap<List<ExportSpecifier>> ExportSpecifiers { get; }

        /// <summary>Exported names of local declarations, indexed by the declaration's original node id.</summary>
        List<List<Identifier>> ExportedBindings { get; }

        /// <summary>All exported names in the module, both local and reexported, excluding the names of locally
        /// exported function declarations.</summary>
        List<Node>? ExportedNames { get; }

        /// <summary>All of the top-level exported function declarations.</summary>
        HashSet<FunctionDeclaration> ExportedFunctions { get; }

        /// <summary>An <c>export=</c> declaration if one was present.</summary>
        ExportAssignment? ExportEquals { get; }

        /// <summary>Whether this module contains <c>export *</c>.</summary>
        bool HasExportStarsToExportValues { get; }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:113-122 — the object literal
    /// <c>collectExternalModuleInfo</c> returns.</summary>
    internal sealed class ExternalModuleInfo(
        List<Node> externalImports,
        IdentifierNameMap<List<ExportSpecifier>> exportSpecifiers,
        ExportAssignment? exportEquals,
        bool hasExportStarsToExportValues,
        List<List<Identifier>> exportedBindings,
        List<Node>? exportedNames,
        HashSet<FunctionDeclaration> exportedFunctions,
        Node? externalHelpersImportDeclaration) : IExternalModuleInfo
    {
        public List<Node> ExternalImports { get; } = externalImports;

        public Node? ExternalHelpersImportDeclaration { get; } = externalHelpersImportDeclaration;

        public IdentifierNameMap<List<ExportSpecifier>> ExportSpecifiers { get; } = exportSpecifiers;

        public List<List<Identifier>> ExportedBindings { get; } = exportedBindings;

        public List<Node>? ExportedNames { get; } = exportedNames;

        public HashSet<FunctionDeclaration> ExportedFunctions { get; } = exportedFunctions;

        public ExportAssignment? ExportEquals { get; } = exportEquals;

        public bool HasExportStarsToExportValues { get; } = hasExportStarsToExportValues;
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:124-128 — <c>containsDefaultReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>NamedImportBindings | NamedExportBindings</c> and its body reads
    /// <c>node.elements</c> after the two kind tests; the port's model gives the two classes the same slot under
    /// different element types, so the read is a switch over the two kinds (the interface-less union's usual
    /// treatment).
    /// </remarks>
    private static bool ContainsDefaultReference(Node? node)
    {
        if (node is null) return false;
        if (!NodeTests.IsNamedImports(node) && !NodeTests.IsNamedExports(node)) return false;
        return node switch
        {
            NamedImports namedImports => Core.Some(namedImports.Elements, e => IsNamedDefaultReference(e)),
            NamedExports namedExports => Core.Some(namedExports.Elements, e => IsNamedDefaultReference(e)),
            _ => false,
        };
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:130-132 — <c>isNamedDefaultReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>e.propertyName || e.name</c> is a node-or-undefined fallback (a node is never falsy), and its
    /// parameter is the union <c>ImportSpecifier | ExportSpecifier</c>; the port reads the two classes' slots by kind.
    /// </remarks>
    private static bool IsNamedDefaultReference(Node e)
        => Utilities.ModuleExportNameIsDefault(e switch
        {
            ImportSpecifier specifier => specifier.PropertyName ?? (Node)specifier.Name!,
            _ => ((ExportSpecifier)e).PropertyName ?? (Node)((ExportSpecifier)e).Name!,
        });

    /// <summary>ported from: src/compiler/transformers/utilities.ts:135-146 — <c>chainBundle</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result type <c>(x: SourceFile | Bundle) =&gt; SourceFile | Bundle</c> is a function whose
    /// parameter is a union; the port's function face is <c>Func&lt;Node, Node&gt;</c> (the same spelling
    /// <c>Utilities.VisitEachChild</c> gives TS's <c>visitEachChild</c>), the union being the <c>kind</c> test in the
    /// returned delegate's body.
    /// </remarks>
    public static Func<Node, Node> ChainBundle(ITransformationUtilitiesContext context, Func<SourceFile, SourceFile> transformSourceFile)
        => node => node.Kind == SyntaxKind.SourceFile
            ? transformSourceFile((SourceFile)node)
            : TransformBundle(context, transformSourceFile, (Bundle)node);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:141-143 — <c>transformBundle</c> (the inner function
    /// of <c>chainBundle</c>).</summary>
    private static Bundle TransformBundle(ITransformationUtilitiesContext context, Func<SourceFile, SourceFile> transformSourceFile, Bundle node)
        => context.Factory.CreateBundle(Core.Map(node.SourceFiles, (sourceFile, _) => transformSourceFile(sourceFile)));

    /// <summary>ported from: src/compiler/transformers/utilities.ts:148-150 — <c>getExportNeedsImportStarHelper</c>.</summary>
    public static bool GetExportNeedsImportStarHelper(ExportDeclaration node)
        => Utilities.GetNamespaceDeclarationNode(node) is not null;

    /// <summary>ported from: src/compiler/transformers/utilities.ts:153-171 — <c>getImportNeedsImportStarHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's trailing test is the truthiness of <c>bindings.elements.length - defaultRefCount</c>, a number in
    /// a boolean position, which the port states as <see cref="Js.IsTruthy(int)"/>.
    /// </remarks>
    public static bool GetImportNeedsImportStarHelper(ImportDeclaration node)
    {
        if (Utilities.GetNamespaceDeclarationNode(node) is not null)
        {
            return true;
        }

        var bindings = node.ImportClause?.NamedBindings;
        if (bindings is null)
        {
            return false;
        }

        if (!NodeTests.IsNamedImports(bindings))
        {
            return false;
        }

        var elements = ((NamedImports)bindings).Elements!;
        var defaultRefCount = 0;
        foreach (var binding in elements)
        {
            if (IsNamedDefaultReference(binding))
            {
                defaultRefCount++;
            }
        }

        // Import star is required if there's default named refs mixed with non-default refs, or if theres non-default
        // refs and it has a default import.
        return (defaultRefCount > 0 && defaultRefCount != elements.Count)
            || (Js.IsTruthy(elements.Count - defaultRefCount) && IsDefaultImport(node));
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:173-177 — <c>getImportNeedsImportDefaultHelper</c>.</summary>
    public static bool GetImportNeedsImportDefaultHelper(ImportDeclaration node)
        // Import default is needed if there's a default import or a default ref and no other refs (meaning an import
        // star helper wasn't requested).
        => !GetImportNeedsImportStarHelper(node)
            && (IsDefaultImport(node)
                || (node.ImportClause is not null
                    && NodeTests.IsNamedImports(node.ImportClause.NamedBindings!)
                    && ContainsDefaultReference(node.ImportClause.NamedBindings)));

    /// <summary>ported from: src/compiler/transformers/utilities.ts:179-353 — <c>collectExternalModuleInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (<c>exportedBindings</c>): TS types the slot <c>Identifier[][]</c> and uses it as a <em>sparse</em> array
    /// indexed by node id; its readers index it directly. The port's <c>List&lt;List&lt;Identifier&gt;&gt;</c> is dense, so
    /// <see cref="MultiMapSparseArrayAdd{V}"/> fills the indices between the current end and the key with nulls — the
    /// same hole-for-hole shape, with <c>null</c> in place of <c>undefined</c>.
    /// </remarks>
    public static IExternalModuleInfo CollectExternalModuleInfo(ITransformationUtilitiesContext context, SourceFile sourceFile)
    {
        var resolver = context.GetEmitResolver();
        var compilerOptions = context.GetCompilerOptions();
        var externalImports = new List<Node>();
        var exportSpecifiers = new IdentifierNameMultiMap<ExportSpecifier>();
        var exportedBindings = new List<List<Identifier>>();
        var uniqueExports = new Dictionary<string, bool>();
        var exportedFunctions = new HashSet<FunctionDeclaration>();
        List<Node>? exportedNames = null;
        var hasExportDefault = false;
        ExportAssignment? exportEquals = null;
        var hasExportStarsToExportValues = false;
        var hasImportStar = false;
        var hasImportDefault = false;

        foreach (var statement in sourceFile.Statements!)
        {
            // PORT-NOTE: the model spells TS's `Statement` as the `IStatement` interface where the list is concerned
            // (`NodeArray<Statement>`), and every read below is one of `utilities.ts`'s node readers; the one cast is
            // what those readers' `Node` parameters need.
            var node = (Node)statement;
            switch (node.Kind)
            {
                case SyntaxKind.ImportDeclaration:
                {
                    // import "mod"
                    // import x from "mod"
                    // import * as x from "mod"
                    // import { x, y } from "mod"
                    var import = (ImportDeclaration)node;
                    externalImports.Add(import);
                    if (!hasImportStar && GetImportNeedsImportStarHelper(import))
                    {
                        hasImportStar = true;
                    }
                    if (!hasImportDefault && GetImportNeedsImportDefaultHelper(import))
                    {
                        hasImportDefault = true;
                    }
                    break;
                }

                case SyntaxKind.ImportEqualsDeclaration:
                    if (((ImportEqualsDeclaration)node).ModuleReference!.Kind == SyntaxKind.ExternalModuleReference)
                    {
                        // import x = require("mod")
                        externalImports.Add(node);
                    }

                    break;

                case SyntaxKind.ExportDeclaration:
                {
                    var exportDeclaration = (ExportDeclaration)node;
                    if (exportDeclaration.ModuleSpecifier is not null)
                    {
                        if (exportDeclaration.ExportClause is null)
                        {
                            // export * from "mod"
                            externalImports.Add(node);
                            hasExportStarsToExportValues = true;
                        }
                        else
                        {
                            // export * as ns from "mod"
                            // export { x, y } from "mod"
                            externalImports.Add(node);
                            if (NodeTests.IsNamedExports(exportDeclaration.ExportClause))
                            {
                                AddExportedNamesForExportDeclaration(exportDeclaration);
                                hasImportDefault = hasImportDefault || ContainsDefaultReference(exportDeclaration.ExportClause);
                            }
                            else
                            {
                                // PORT-NOTE: the model's `NamespaceExport.name` is a `MemberExpression` where the
                                // `exportedBindings` slot is `Identifier[][]`; TS's own types let the wider
                                // `ModuleExportName` through, and `export * as ns` can only spell `ns` as an identifier.
                                var name = (Identifier)((NamespaceExport)exportDeclaration.ExportClause).Name!;
                                var nameText = Utilities.ModuleExportNameTextUnescaped(name);
                                if (!uniqueExports.GetValueOrDefault(nameText))
                                {
                                    MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId(node), name);
                                    uniqueExports[nameText] = true;
                                    exportedNames = Append(exportedNames, name);
                                }
                                // we use the same helpers for `export * as ns` as we do for `import * as ns`
                                hasImportStar = true;
                            }
                        }
                    }
                    else
                    {
                        // export { x, y }
                        AddExportedNamesForExportDeclaration(exportDeclaration);
                    }
                    break;
                }

                case SyntaxKind.ExportAssignment:
                    if (Js.IsTruthy(((ExportAssignment)node).IsExportEquals) && exportEquals is null)
                    {
                        // export = x
                        exportEquals = (ExportAssignment)node;
                    }
                    break;

                case SyntaxKind.VariableStatement:
                    if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
                    {
                        foreach (var decl in ((VariableStatement)node).DeclarationList!.Declarations!)
                        {
                            exportedNames = CollectExportedVariableInfo(decl, uniqueExports, exportedNames, exportedBindings);
                        }
                    }
                    break;

                case SyntaxKind.FunctionDeclaration:
                    if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
                    {
                        AddExportedFunctionDeclaration((FunctionDeclaration)node, /*name*/ null, Utilities.HasSyntacticModifier(node, ModifierFlags.Default));
                    }
                    break;

                case SyntaxKind.ClassDeclaration:
                    if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
                    {
                        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Default))
                        {
                            // export default class { }
                            if (!hasExportDefault)
                            {
                                MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId(node), GetDeclarationName(context.Factory, node));
                                hasExportDefault = true;
                            }
                        }
                        else
                        {
                            // export class x { }
                            var name = ((ClassDeclaration)node).Name;
                            if (name is not null && !uniqueExports.GetValueOrDefault(Utilities.IdText(name)))
                            {
                                MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId(node), name);
                                uniqueExports[Utilities.IdText(name)] = true;
                                exportedNames = Append(exportedNames, name);
                            }
                        }
                    }
                    break;
            }
        }

        var externalHelpersImportDeclaration = CreateExternalHelpersImportDeclarationIfNeeded(
            context.Factory,
            context.GetEmitHelperFactory(),
            sourceFile,
            compilerOptions,
            hasExportStarsToExportValues,
            hasImportStar,
            hasImportDefault);
        if (externalHelpersImportDeclaration is not null)
        {
            externalImports.Insert(0, externalHelpersImportDeclaration);
        }

        return new ExternalModuleInfo(
            externalImports,
            exportSpecifiers,
            exportEquals,
            hasExportStarsToExportValues,
            exportedBindings,
            exportedNames,
            exportedFunctions,
            externalHelpersImportDeclaration);

        void AddExportedNamesForExportDeclaration(ExportDeclaration node)
        {
            foreach (var specifier in ((NamedExports)node.ExportClause!).Elements!)
            {
                var specifierNameText = Utilities.ModuleExportNameTextUnescaped(specifier.Name);
                if (!uniqueExports.GetValueOrDefault(specifierNameText))
                {
                    var name = specifier.PropertyName ?? (Node)specifier.Name!;
                    if (name.Kind != SyntaxKind.StringLiteral)
                    {
                        if (node.ModuleSpecifier is null)
                        {
                            exportSpecifiers.Add(name, specifier);
                        }

                        var decl = resolver.GetReferencedImportDeclaration((Identifier)name)
                            ?? resolver.GetReferencedValueDeclaration((Identifier)name);

                        if (decl is not null)
                        {
                            if (decl.Kind == SyntaxKind.FunctionDeclaration)
                            {
                                AddExportedFunctionDeclaration((FunctionDeclaration)decl, (Identifier)specifier.Name!, Utilities.ModuleExportNameIsDefault(specifier.Name!));
                                continue;
                            }

                            MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId((Node)decl), (Identifier)specifier.Name!);
                        }
                    }

                    uniqueExports[specifierNameText] = true;
                    exportedNames = Append(exportedNames, specifier.Name);
                }
            }
        }

        void AddExportedFunctionDeclaration(FunctionDeclaration node, Identifier? name, bool isDefault)
        {
            // PORT-NOTE: the reference declares this parameter as the `ModuleExportName | undefined` union while the
            // `exportedBindings` slot it feeds is `Identifier[][]`; the port states the type the slot needs (the model's
            // `ExportSpecifier.name` slot is a `MemberExpression`), and the two call sites pass the identifier the
            // reference's own guard licenses.
            exportedFunctions.Add((FunctionDeclaration)Utilities.GetOriginalNode(node, NodeTests.IsFunctionDeclaration)!);
            if (isDefault)
            {
                // export default function() { }
                // function x() { } + export { x as default };
                if (!hasExportDefault)
                {
                    MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId(node), name ?? GetDeclarationName(context.Factory, node));
                    hasExportDefault = true;
                }
            }
            else
            {
                // export function x() { }
                // function x() { } + export { x }
                name ??= node.Name!;
                var nameText = Utilities.ModuleExportNameTextUnescaped(name);
                if (!uniqueExports.GetValueOrDefault(nameText))
                {
                    MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId(node), name);
                    uniqueExports[nameText] = true;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:355-375 — <c>collectExportedVariableInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>VariableDeclaration | BindingElement</c> and its body reads
    /// <c>decl.name</c>; the port takes the union's common base (<see cref="Node"/>) and reads the same slot through the
    /// two kinds, which is also the shape the recursion's <c>BindingElement</c> argument needs.
    /// </remarks>
    private static List<Node>? CollectExportedVariableInfo(
        Node decl,
        Dictionary<string, bool> uniqueExports,
        List<Node>? exportedNames,
        List<List<Identifier>> exportedBindings)
    {
        var name = decl switch
        {
            VariableDeclaration variable => variable.Name,
            BindingElement element => element.Name,
            _ => null,
        };

        if (name is not null && Utilities.IsBindingPattern(name))
        {
            IEnumerable<Node>? elements = name switch
            {
                ObjectBindingPattern pattern => pattern.Elements,
                ArrayBindingPattern pattern => pattern.Elements,
                _ => null,
            };

            if (elements is not null)
            {
                foreach (var element in elements)
                {
                    if (!NodeTests.IsOmittedExpression(element))
                    {
                        exportedNames = CollectExportedVariableInfo(element, uniqueExports, exportedNames, exportedBindings);
                    }
                }
            }
        }
        else if (name is not null && !Utilities.IsGeneratedIdentifier(name))
        {
            var text = Utilities.IdText(name);
            if (!uniqueExports.GetValueOrDefault(text))
            {
                uniqueExports[text] = true;
                exportedNames = Append(exportedNames, name);
                if (FactoryUtilities.IsLocalName(name))
                {
                    MultiMapSparseArrayAdd(exportedBindings, GetOriginalNodeId(decl), (Identifier)name);
                }
            }
        }

        return exportedNames;
    }

    /// <summary>ported from: src/compiler/core.ts:315-323 — <c>append</c>, stated in the concrete list type its readers
    /// accumulate.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>append</c> is the landed <see cref="Core.Append{T}"/>, whose answer is the
    /// <c>IList&lt;T&gt;?</c> it takes; every accumulator in this file is a <c>List&lt;T&gt;</c> (the reference's
    /// <c>T[]</c>), so this private adapter is that member with the reference's own return type — no behaviour of its own.
    /// </remarks>
    private static List<T>? Append<T>(List<T>? to, T? value)
        where T : class
        => (List<T>?)Core.Append(to, value);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:377-386 — <c>multiMapSparseArrayAdd</c> (a sparse
    /// array used as a multi-map).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>map[key]</c> on an array with holes answers <c>undefined</c>; the port's <c>List</c> must first
    /// be grown to the key, with <c>null</c> in the holes — which is what the readers see for an absent entry.
    /// </remarks>
    private static List<V> MultiMapSparseArrayAdd<V>(List<List<V>> map, int key, V value)
    {
        while (map.Count <= key)
        {
            map.Add(null!);
        }

        var values = map[key];
        if (values is not null)
        {
            values.Add(value);
        }
        else
        {
            map[key] = values = [value];
        }

        return values;
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:389-469 — IdentifierNameMap / IdentifierNameMultiMap
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:389-439 — <c>IdentifierNameMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's key type is the union <c>Identifier | PrivateIdentifier</c> and its store is a
    /// <c>Map&lt;string, V&gt;</c> whose key is the name's <em>generated</em> spelling; the port takes <see cref="Node"/>
    /// (the union has no C# spelling) and a <c>Dictionary&lt;string, V&gt;</c> with ordinal keys, which is what
    /// <c>Map</c>'s string keys are. <c>get size()</c> is <see cref="Count"/>, <c>values(): MapIterator&lt;V&gt;</c> is
    /// <see cref="Values"/>, and the reference's <c>this._map?.delete(...) ?? false</c> is <c>Dictionary.Remove</c>'s
    /// own answer.
    /// </remarks>
    internal class IdentifierNameMap<V>
    {
        private readonly Dictionary<string, V> _map = [];

        public int Count => _map.Count;

        public bool Has(Node key) => _map.ContainsKey(ToKey(key));

        public V? Get(Node key) => _map.TryGetValue(ToKey(key), out var value) ? value : default;

        public IdentifierNameMap<V> Set(Node key, V value)
        {
            _map[ToKey(key)] = value;
            return this;
        }

        public bool Delete(Node key) => _map.Remove(ToKey(key));

        public void Clear() => _map.Clear();

        public IEnumerable<V> Values() => _map.Values;

        /// <remarks>
        /// PORT-NOTE: TS reads <c>name.emitNode.autoGenerate</c> without a guard — a generated identifier always carries
        /// one — so the port's two <c>!</c> are that same non-null assertion rather than a check the reference does not make.
        /// </remarks>
        private static string ToKey(Node name)
        {
            if (Utilities.IsGeneratedPrivateIdentifier(name) || Utilities.IsGeneratedIdentifier(name))
            {
                var autoGenerate = name.EmitNode!.AutoGenerate!;
                if ((autoGenerate.Flags & GeneratedIdentifierFlags.KindMask) == GeneratedIdentifierFlags.Node)
                {
                    var node = FactoryUtilities.GetNodeForGeneratedName(name);
                    var baseName = Utilities.IsMemberName(node) && !ReferenceEquals(node, name)
                        ? ToKey(node)
                        : $"(generated@{Utilities.GetNodeId(node)})";
                    return FactoryUtilities.FormatGeneratedName(/*privateName*/ false, autoGenerate.Prefix, baseName, autoGenerate.Suffix, ToKey);
                }
                else
                {
                    var baseName = $"(auto@{autoGenerate.Id})";
                    return FactoryUtilities.FormatGeneratedName(/*privateName*/ false, autoGenerate.Prefix, baseName, autoGenerate.Suffix, ToKey);
                }
            }
            if (NodeTests.IsPrivateIdentifier(name))
            {
                return Utilities.IdText(name)[1..];
            }
            return Utilities.IdText(name);
        }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:441-469 — <c>IdentifierNameMultiMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>extends IdentifierNameMap&lt;V[]&gt;</c>, which the port spells
    /// <c>IdentifierNameMap&lt;List&lt;V&gt;&gt;</c> (the <c>T[]</c>→<c>List&lt;T&gt;</c> mapping of §12.3).
    /// </remarks>
    internal sealed class IdentifierNameMultiMap<V> : IdentifierNameMap<List<V>>
    {
        /// <summary>ported from: src/compiler/transformers/utilities.ts:442-451 — <c>add</c>.</summary>
        public List<V> Add(Node key, V value)
        {
            var values = Get(key);
            if (values is not null)
            {
                values.Add(value);
            }
            else
            {
                Set(key, values = [value]);
            }
            return values;
        }

        /// <summary>ported from: src/compiler/transformers/utilities.ts:453-461 — <c>remove</c>.</summary>
        public void Remove(Node key, V value)
        {
            var values = Get(key);
            if (values is not null)
            {
                Core.UnorderedRemoveItem(values, value);
                if (values.Count == 0)
                {
                    Delete(key);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:471-534 — the expression/operator predicates
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:471-477 — <c>isSimpleCopiableExpression</c>.</summary>
    public static bool IsSimpleCopiableExpression(Expression expression)
        => Utilities.IsStringLiteralLike(expression)
            || expression.Kind == SyntaxKind.NumericLiteral
            || Utilities.IsKeyword(expression.Kind)
            || NodeTests.IsIdentifier(expression);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:485-487 — <c>isSimpleInlineableExpression</c>.</summary>
    public static bool IsSimpleInlineableExpression(Expression expression)
        => !NodeTests.IsIdentifier(expression) && IsSimpleCopiableExpression(expression);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:490-494 — <c>isCompoundAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>BinaryOperator</c> union of operator kinds and its return is a type guard; the
    /// port takes <see cref="SyntaxKind"/> (the union's representation, §16) and answers <c>bool</c> (mapping rule (d)).
    /// </remarks>
    public static bool IsCompoundAssignment(SyntaxKind kind)
        => kind >= SyntaxKind.FirstCompoundAssignment
            && kind <= SyntaxKind.LastCompoundAssignment;

    /// <summary>ported from: src/compiler/transformers/utilities.ts:496-534 — <c>getNonAssignmentOperatorForCompoundAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is <c>LogicalOperatorOrHigher | SyntaxKind.QuestionQuestionToken</c> — every member an
    /// operator kind — so the port answers <see cref="SyntaxKind"/>.
    /// </remarks>
    public static SyntaxKind GetNonAssignmentOperatorForCompoundAssignment(SyntaxKind kind)
        => kind switch
        {
            SyntaxKind.PlusEqualsToken => SyntaxKind.PlusToken,
            SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
            SyntaxKind.AsteriskEqualsToken => SyntaxKind.AsteriskToken,
            SyntaxKind.AsteriskAsteriskEqualsToken => SyntaxKind.AsteriskAsteriskToken,
            SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
            SyntaxKind.PercentEqualsToken => SyntaxKind.PercentToken,
            SyntaxKind.LessThanLessThanEqualsToken => SyntaxKind.LessThanLessThanToken,
            SyntaxKind.GreaterThanGreaterThanEqualsToken => SyntaxKind.GreaterThanGreaterThanToken,
            SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken => SyntaxKind.GreaterThanGreaterThanGreaterThanToken,
            SyntaxKind.AmpersandEqualsToken => SyntaxKind.AmpersandToken,
            SyntaxKind.BarEqualsToken => SyntaxKind.BarToken,
            SyntaxKind.CaretEqualsToken => SyntaxKind.CaretToken,
            SyntaxKind.BarBarEqualsToken => SyntaxKind.BarBarToken,
            SyntaxKind.AmpersandAmpersandEqualsToken => SyntaxKind.AmpersandAmpersandToken,
            SyntaxKind.QuestionQuestionEqualsToken => SyntaxKind.QuestionQuestionToken,
            // PORT-NOTE: TS's switch has no default and its declared return type is not `never`, so an unmatched kind
            // answers `undefined`; the port cannot return "no kind" from a SyntaxKind, so it throws — reachable only for a
            // binary operator that is not a compound assignment, which is what isCompoundAssignment gates.
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a compound assignment operator."),
        };

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:536-578 — the super() search
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:536-545 — <c>getSuperCallFromStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Statement</c> and its answer <c>SuperCall</c>; the port takes <see cref="Node"/>
    /// because the search below walks a <c>NodeArray&lt;Statement&gt;</c> the model spells as
    /// <c>NodeArray&lt;IStatement&gt;</c>, and answers <see cref="CallExpression"/> because TS's <c>SuperCall</c> is the
    /// alias <c>CallExpression &amp; { expression: SuperExpression }</c> — the model carries it only as the empty
    /// interface <c>ISuperCall</c>, which no class implements, so <c>CallExpression</c> is what
    /// <see cref="Utilities.IsSuperCall"/> licenses (its own PORT-NOTE records the same ruling).
    /// </remarks>
    public static CallExpression? GetSuperCallFromStatement(Node statement)
    {
        if (!NodeTests.IsExpressionStatement(statement))
        {
            return null;
        }

        var expression = Utilities.SkipParentheses(((ExpressionStatement)statement).Expression!);
        return Utilities.IsSuperCall(expression)
            ? (CallExpression)expression
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:547-562 — <c>findSuperStatementIndexPathWorker</c>.</summary>
    private static bool FindSuperStatementIndexPathWorker(NodeArray<IStatement> statements, int start, List<int> indices)
    {
        for (var i = start; i < statements.Count; i += 1)
        {
            // PORT-NOTE: the list's element type is the model's `IStatement` (see the collect loop); the two readers
            // below take a `Node`.
            var statement = (Node)statements[i];
            if (GetSuperCallFromStatement(statement) is not null)
            {
                indices.Insert(0, i);
                return true;
            }
            else if (NodeTests.IsTryStatement(statement) && FindSuperStatementIndexPathWorker(((TryStatement)statement).TryBlock!.Statements!, 0, indices))
            {
                indices.Insert(0, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:571-578 — <c>findSuperStatementIndexPath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>number[]</c>; the port's <c>List&lt;int&gt;</c> is that array. TS's parameter is
    /// <c>NodeArray&lt;Statement&gt;</c>.
    /// </remarks>
    public static List<int> FindSuperStatementIndexPath(NodeArray<IStatement> statements, int start)
    {
        var indices = new List<int>();
        FindSuperStatementIndexPathWorker(statements, start, indices);
        return indices;
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:581-649 — the class-member queries
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:585-591 — <c>getProperties</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS states three signatures — one with <c>requireInitializer: true</c> answering
    /// <c>readonly InitializedPropertyDeclaration[]</c>, one with <c>requireInitializer: boolean</c> answering
    /// <c>readonly PropertyDeclaration[]</c>, and the implementation. C# has no literal boolean type and the port's model
    /// has no <c>InitializedPropertyDeclaration</c> (it is TS's <c>PropertyDeclaration &amp; { initializer }</c>), so the
    /// two declarations collapse into this one member; a caller that needs the narrower reading tests
    /// <see cref="IsInitializedProperty"/> (which is what the reference's guard means). TS's parameter is
    /// <c>ClassExpression | ClassDeclaration</c>, which the port spells <see cref="IClassLikeDeclarationBase"/>.
    /// The reference's <c>filter(...) as PropertyDeclaration[]</c> is a fresh array, so the port projects (the landed
    /// <c>Core.Filter</c> answers the input object when nothing is dropped, which that cast must not preserve).
    /// </remarks>
    public static IReadOnlyList<PropertyDeclaration> GetProperties(Node node, bool requireInitializer, bool isStatic)
        => [.. Core.Filter(((IClassLikeDeclarationBase)node).Members, m => IsInitializedOrStaticProperty(m, requireInitializer, isStatic))!.Cast<PropertyDeclaration>()];

    /// <summary>ported from: src/compiler/transformers/utilities.ts:593-595 — <c>isStaticPropertyDeclarationOrClassStaticBlockDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>ClassElement</c> union and its result a type guard, which the port
    /// states as the model's <see cref="IClassElement"/> (the union's interface) answering <c>bool</c>; the two readers
    /// it delegates to take a <c>Node</c>.</remarks>
    private static bool IsStaticPropertyDeclarationOrClassStaticBlockDeclaration(IClassElement element)
        => IsStaticPropertyDeclaration(element) || NodeTests.IsClassStaticBlockDeclaration((Node)element);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:598-606 — <c>getStaticPropertiesAndClassStaticBlock</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="GetProperties"/>: the reference's three identical declarations collapse into one,
    /// and the answer's union (<c>PropertyDeclaration | ClassStaticBlockDeclaration</c>) is the model's
    /// <see cref="IClassElement"/>.</remarks>
    public static IReadOnlyList<IClassElement> GetStaticPropertiesAndClassStaticBlock(Node node)
        => [.. Core.Filter(((IClassLikeDeclarationBase)node).Members, IsStaticPropertyDeclarationOrClassStaticBlockDeclaration)!.Cast<IClassElement>()];

    /// <summary>ported from: src/compiler/transformers/utilities.ts:608-616 — <c>isInitializedOrStaticProperty</c>.</summary>
    private static bool IsInitializedOrStaticProperty(IClassElement member, bool requireInitializer, bool isStatic)
        => NodeTests.IsPropertyDeclaration((Node)member)
            && (Js.IsTruthy(((PropertyDeclaration)member).Initializer) || !requireInitializer)
            && Utilities.HasStaticModifier((Node)member) == isStatic;

    /// <summary>ported from: src/compiler/transformers/utilities.ts:618-620 — <c>isStaticPropertyDeclaration</c>.</summary>
    private static bool IsStaticPropertyDeclaration(IClassElement member)
        => NodeTests.IsPropertyDeclaration((Node)member) && Utilities.HasStaticModifier((Node)member);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:630-635 — <c>isInitializedProperty</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard narrows to <c>PropertyDeclaration &amp; { initializer: Expression }</c>; the port
    /// answers <c>bool</c> (mapping rule (d)), and a caller that needs the initializer reads
    /// <c>((PropertyDeclaration)member).Initializer</c> after this test — the same read the reference's guard licenses.</remarks>
    public static bool IsInitializedProperty(IClassElement member)
        => member.Kind == SyntaxKind.PropertyDeclaration
            && ((PropertyDeclaration)member).Initializer is not null;

    /// <summary>ported from: src/compiler/transformers/utilities.ts:642-644 — <c>isNonStaticMethodOrAccessorWithPrivateName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>ClassElement</c> union and its result a type guard over
    /// <c>PrivateIdentifierMethodDeclaration | PrivateIdentifierAccessorDeclaration | PrivateIdentifierAutoAccessorPropertyDeclaration</c>;
    /// the port takes <see cref="IClassElement"/> and answers <c>bool</c> (the three names are one model shape — a method,
    /// accessor or auto-accessor property whose <c>name</c> is a <see cref="PrivateIdentifier"/>).</remarks>
    public static bool IsNonStaticMethodOrAccessorWithPrivateName(IClassElement member)
        => !Utilities.IsStatic((Node)member)
            && (Utilities.IsMethodOrAccessor((Node)member) || Utilities.IsAutoAccessorPropertyDeclaration((Node)member))
            && NodeTests.IsPrivateIdentifier(member.Name!);

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:652-786 — the decorator queries
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:652-676 — <c>getDecoratorsOfParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is <c>(readonly Decorator[] | undefined)[] | undefined</c> — an array whose slots may be
    /// <c>undefined</c> for a parameter without decorators, allocated by <c>new Array(numParameters)</c> (holes) — and the
    /// port's <c>List&lt;List&lt;Decorator&gt;?&gt;</c> is that array with nulls in the holes. TS's parameter is
    /// <c>FunctionLikeDeclaration | undefined</c>, which the port spells <see cref="IFunctionLikeDeclarationBase"/>.
    /// </remarks>
    private static List<List<Decorator>?>? GetDecoratorsOfParameters(IFunctionLikeDeclarationBase? node)
    {
        List<List<Decorator>?>? decorators = null;
        if (node is not null)
        {
            var parameters = node.Parameters!;
            var firstParameterIsThis = parameters.Count > 0 && Utilities.ParameterIsThisKeyword(parameters[0]);
            var firstParameterOffset = firstParameterIsThis ? 1 : 0;
            var numParameters = firstParameterIsThis ? parameters.Count - 1 : parameters.Count;
            for (var i = 0; i < numParameters; i++)
            {
                var parameter = parameters[i + firstParameterOffset];
                if (decorators is not null || Utilities.HasDecorators(parameter))
                {
                    if (decorators is null)
                    {
                        decorators = new List<List<Decorator>?>(numParameters);
                        for (var j = 0; j < numParameters; j++)
                        {
                            decorators.Add(null);
                        }
                    }

                    decorators[i] = Utilities.GetDecorators(parameter);
                }
            }
        }

        return decorators;
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:682-693 — <c>getAllDecoratorsOfClass</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node</c> is the <c>ClassLikeDeclaration</c> union, whose two members share no class in the
    /// model, so the port takes the common base (<see cref="Node"/>). The answer's <c>parameters</c> slot is the model's
    /// <c>List&lt;List&lt;Decorator&gt;&gt;</c> while the reference's array may hold <c>undefined</c> per parameter, so
    /// the one cast below states that hole.
    /// </remarks>
    public static IAllDecorators? GetAllDecoratorsOfClass(Node node, bool useLegacyDecorators)
    {
        var decorators = Utilities.GetDecorators(node);
        var parameters = useLegacyDecorators ? GetDecoratorsOfParameters(Utilities.GetFirstConstructorWithBody(node)) : null;
        if (!Core.Some(decorators) && !Core.Some(parameters))
        {
            return null;
        }

        return new AllDecorators
        {
            Decorators = decorators,
            Parameters = parameters is null ? null : (List<List<Decorator>>)(object)parameters,
        };
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:698-724 — <c>getAllDecoratorsOfClassElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>member</c> is the <c>ClassElement</c> union and <c>parent</c> is the
    /// <c>ClassLikeDeclaration</c> union; the port takes the model's <see cref="IClassElement"/> (the union's interface)
    /// and the <see cref="Node"/> that is <c>ClassLikeDeclaration</c>'s common base.</remarks>
    public static IAllDecorators? GetAllDecoratorsOfClassElement(IClassElement member, Node parent, bool useLegacyDecorators)
    {
        switch (member.Kind)
        {
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                if (!useLegacyDecorators)
                {
                    return GetAllDecoratorsOfMethod((Node)member, /*useLegacyDecorators*/ false);
                }
                return GetAllDecoratorsOfAccessors((IFunctionLikeDeclarationBase)member, parent, /*useLegacyDecorators*/ true);

            case SyntaxKind.MethodDeclaration:
                return GetAllDecoratorsOfMethod((MethodDeclaration)member, useLegacyDecorators);

            case SyntaxKind.PropertyDeclaration:
                return GetAllDecoratorsOfProperty((PropertyDeclaration)member);

            default:
                return null;
        }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:729-757 — <c>getAllDecoratorsOfAccessors</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the accessor search): the reference calls <c>getAllAccessorDeclarations(parent.members, accessor)</c>,
    /// declared by <c>utilities.ts:6832-6885</c>; the port's own copy of that function is a local function inside
    /// <c>Utilities.ClassElementOrClassElementParameterIsDecorated</c> (Utilities/Utilities.Checker.cs:2457) and is not
    /// reachable, and §15.15 forbids <c>Utilities</c> from taking the <c>GetAllAccessorDeclarations</c> name (the
    /// <c>ITypeChecker</c> surface owns it as <c>GetAllAccessorDeclarationsForDeclaration</c>), so
    /// <see cref="GetAllAccessorDeclarations"/> below is that reference body again, private to this class. Consolidating
    /// the two copies is listed in the batch report.
    /// </remarks>
    private static IAllDecorators? GetAllDecoratorsOfAccessors(IFunctionLikeDeclarationBase accessor, Node parent, bool useLegacyDecorators)
    {
        if (accessor.Body is null)
        {
            return null;
        }

        var (firstAccessor, secondAccessor, getAccessor, setAccessor) = GetAllAccessorDeclarations(((IClassLikeDeclarationBase)parent).Members, accessor);
        var firstAccessorWithDecorators = firstAccessor is not null && Utilities.HasDecorators(firstAccessor) ? firstAccessor
            : secondAccessor is not null && Utilities.HasDecorators(secondAccessor) ? secondAccessor
            : null;

        if (firstAccessorWithDecorators is null || !ReferenceEquals(accessor, firstAccessorWithDecorators))
        {
            return null;
        }

        var decorators = Utilities.GetDecorators(firstAccessorWithDecorators);
        var parameters = useLegacyDecorators ? GetDecoratorsOfParameters(setAccessor) : null;
        if (!Core.Some(decorators) && !Core.Some(parameters))
        {
            return null;
        }

        return new AllDecorators
        {
            Decorators = decorators,
            Parameters = parameters is null ? null : (List<List<Decorator>>)(object)parameters,
            GetDecorators = getAccessor is not null ? Utilities.GetDecorators(getAccessor) : null,
            SetDecorators = setAccessor is not null ? Utilities.GetDecorators(setAccessor) : null,
        };
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:762-776 — <c>getAllDecoratorsOfMethod</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>MethodDeclaration | AccessorDeclaration</c>, whose two members
    /// share the model's <see cref="IFunctionLikeDeclarationBase"/> interface but not a class; the port takes the common
    /// base (<see cref="Node"/>) for the decorator read and that interface for the two slots it reads from the
    /// function-like shape.</remarks>
    private static IAllDecorators? GetAllDecoratorsOfMethod(Node method, bool useLegacyDecorators)
    {
        if (((IFunctionLikeDeclarationBase)method).Body is null)
        {
            return null;
        }

        var decorators = Utilities.GetDecorators(method);
        var parameters = useLegacyDecorators ? GetDecoratorsOfParameters((IFunctionLikeDeclarationBase)method) : null;
        if (!Core.Some(decorators) && !Core.Some(parameters))
        {
            return null;
        }

        return new AllDecorators
        {
            Decorators = decorators,
            Parameters = parameters is null ? null : (List<List<Decorator>>)(object)parameters,
        };
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:781-786 — <c>getAllDecoratorsOfProperty</c>.</summary>
    private static IAllDecorators? GetAllDecoratorsOfProperty(PropertyDeclaration property)
    {
        var decorators = Utilities.GetDecorators(property);
        if (!Core.Some(decorators))
        {
            return null;
        }

        return new AllDecorators { Decorators = decorators };
    }

    /// <summary>ported from: src/compiler/types.ts:5818-5823 via src/compiler/transformers/utilities.ts:682-786 — the
    /// <c>{ decorators, parameters, getDecorators, setDecorators }</c> object literal those five functions fill in.</summary>
    /// <remarks>
    /// PORT-NOTE: the model carries only <see cref="IAllDecorators"/> (Model/NodeInterfaces.cs:23), so this nested class is
    /// the port's constructible implementation — the same split as <c>Checker.Check.Flags.cs</c>'s private
    /// <c>AllAccessorDeclarations</c>. <c>Parameters</c> keeps the generated slot's type; the reference's per-parameter
    /// holes are nulls in it (see <see cref="GetDecoratorsOfParameters"/>).
    /// </remarks>
    private sealed class AllDecorators : IAllDecorators
    {
        public List<Decorator>? Decorators { get; set; }

        public List<List<Decorator>>? Parameters { get; set; }

        public List<Decorator>? GetDecorators { get; set; }

        public List<Decorator>? SetDecorators { get; set; }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6832-6885 — <c>getAllAccessorDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the second copy of that reference body in the port (see <see cref="GetAllDecoratorsOfAccessors"/>); its
    /// first, in <c>Utilities</c>, is a local function and unreachable. TS's first parameter is
    /// <c>readonly Declaration[] | undefined</c> and its answer the <c>AllAccessorDeclarations</c> interface, which the
    /// model carries as <see cref="IAllAccessorDeclarations"/>; the port answers the same four slots as a tuple because
    /// the caller destructures them and the tuple is what TS's object literal is for.
    /// </remarks>
    private static (Node? FirstAccessor, Node? SecondAccessor, GetAccessorDeclaration? GetAccessor, SetAccessorDeclaration? SetAccessor)
        GetAllAccessorDeclarations(IEnumerable<IClassElement>? declarations, IFunctionLikeDeclarationBase accessor)
    {
        Node? firstAccessor = null;
        Node? secondAccessor = null;
        GetAccessorDeclaration? getAccessor = null;
        SetAccessorDeclaration? setAccessor = null;
        if (Utilities.HasDynamicName((Node)accessor))
        {
            firstAccessor = (Node)accessor;
            if (accessor.Kind == SyntaxKind.GetAccessor)
            {
                getAccessor = (GetAccessorDeclaration)accessor;
            }
            else if (accessor.Kind == SyntaxKind.SetAccessor)
            {
                setAccessor = (SetAccessorDeclaration)accessor;
            }
            else
            {
                Debug.Fail("Accessor has wrong kind");
            }
        }
        else
        {
            foreach (var member in declarations ?? [])
            {
                var m = (Node)member;
                if (!Utilities.IsAccessor(m) || Utilities.IsStatic(m) != Utilities.IsStatic((Node)accessor))
                {
                    continue;
                }
                var memberName = Utilities.GetPropertyNameForPropertyNameNode(member.Name!);
                var accessorName = Utilities.GetPropertyNameForPropertyNameNode(accessor.Name!);
                if (memberName == accessorName)
                {
                    if (firstAccessor is null)
                    {
                        firstAccessor = m;
                    }
                    else if (secondAccessor is null)
                    {
                        secondAccessor = m;
                    }

                    if (m.Kind == SyntaxKind.GetAccessor && getAccessor is null)
                    {
                        getAccessor = (GetAccessorDeclaration)m;
                    }

                    if (m.Kind == SyntaxKind.SetAccessor && setAccessor is null)
                    {
                        setAccessor = (SetAccessorDeclaration)m;
                    }
                }
            }
        }
        return (firstAccessor, secondAccessor, getAccessor, setAccessor);
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:791-864 — the private/lexical environments
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:791-804 — <c>PrivateEnvironment</c>.</summary>
    /// <remarks>PORT-NOTE: the model has no generated interface for this type (it is declared by
    /// <c>transformers/utilities.ts</c>, not by <c>types.ts</c>), so the port states it here with the reference's own
    /// name. TS's <c>Map&lt;__String, TEntry&gt;</c> is the port's <c>Dictionary&lt;string, TEntry&gt;</c>
    /// (<c>__String</c> is a plain string in the model).</remarks>
    internal interface PrivateEnvironment<TData, TEntry>
    {
        /// <summary>The environment's data.</summary>
        TData Data { get; }

        /// <summary>A mapping of private names to information needed for transformation.</summary>
        Dictionary<string, TEntry>? Identifiers { get; set; }

        /// <summary>A mapping of generated private names to information needed for transformation.</summary>
        Dictionary<Node, TEntry>? GeneratedIdentifiers { get; set; }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:806-810 — <c>LexicalEnvironment</c>.</summary>
    internal interface LexicalEnvironment<TEnvData, TPrivateEnvData, TPrivateEntry>
    {
        TEnvData Data { get; set; }

        PrivateEnvironment<TPrivateEnvData, TPrivateEntry>? PrivateEnv { get; set; }

        LexicalEnvironment<TEnvData, TPrivateEnvData, TPrivateEntry>? Previous { get; }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:812-822 — <c>walkUpLexicalEnvironments</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>cb</c> answers <c>U | undefined</c> and the walk stops at the first non-<c>undefined</c>
    /// answer; the port's <c>Func&lt;T, U?&gt;</c> with a reference-type <c>U</c> is that same test (<c>is not null</c>), and
    /// <c>U</c> carries the <c>class</c> constraint two <c>?</c>-typed generic slots need.</remarks>
    private static U? WalkUpLexicalEnvironments<TEnvData, TPrivateEnvData, TPrivateEntry, U>(
        LexicalEnvironment<TEnvData, TPrivateEnvData, TPrivateEntry>? env,
        Func<LexicalEnvironment<TEnvData, TPrivateEnvData, TPrivateEntry>, U?> cb)
        where U : class
    {
        while (env is not null)
        {
            var result = cb(env);
            if (result is not null) return result;
            env = env.Previous;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:824-826 — <c>newPrivateEnvironment</c>.</summary>
    /// <remarks>PORT-NOTE: TS returns the object literal <c>{ data }</c>; the port needs a constructible type, which is
    /// <see cref="PrivateEnvironmentImpl{TData,TEntry}"/> — the same pairing as
    /// <see cref="IExternalModuleInfo"/>/<see cref="ExternalModuleInfo"/>.</remarks>
    public static PrivateEnvironment<TData, TEntry> NewPrivateEnvironment<TData, TEntry>(TData data)
        => new PrivateEnvironmentImpl<TData, TEntry>(data);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:791-804 — the <c>{ data }</c> object literal
    /// <c>newPrivateEnvironment</c> returns.</summary>
    private sealed class PrivateEnvironmentImpl<TData, TEntry>(TData data) : PrivateEnvironment<TData, TEntry>
    {
        public TData Data { get; } = data;

        public Dictionary<string, TEntry>? Identifiers { get; set; }

        public Dictionary<Node, TEntry>? GeneratedIdentifiers { get; set; }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:829-837 — <c>getPrivateIdentifier</c>.</summary>
    public static TEntry? GetPrivateIdentifier<TData, TEntry>(
        PrivateEnvironment<TData, TEntry>? privateEnv,
        PrivateIdentifier name)
        => Utilities.IsGeneratedPrivateIdentifier(name)
            ? privateEnv?.GeneratedIdentifiers is { } generatedIdentifiers && generatedIdentifiers.TryGetValue(FactoryUtilities.GetNodeForGeneratedName(name), out var entry) ? entry : default
            : privateEnv?.Identifiers is { } identifiers && identifiers.TryGetValue(name.EscapedText!, out var entry2) ? entry2 : default;

    /// <summary>ported from: src/compiler/transformers/utilities.ts:839-853 — <c>setPrivateIdentifier</c>.</summary>
    public static void SetPrivateIdentifier<TData, TEntry>(
        PrivateEnvironment<TData, TEntry> privateEnv,
        PrivateIdentifier name,
        TEntry entry)
    {
        if (Utilities.IsGeneratedPrivateIdentifier(name))
        {
            privateEnv.GeneratedIdentifiers ??= [];
            privateEnv.GeneratedIdentifiers[FactoryUtilities.GetNodeForGeneratedName(name)] = entry;
        }
        else
        {
            privateEnv.Identifiers ??= [];
            privateEnv.Identifiers[name.EscapedText!] = entry;
        }
    }

    /// <summary>ported from: src/compiler/transformers/utilities.ts:855-864 — <c>accessPrivateIdentifier</c>.</summary>
    public static TPrivateEntry? AccessPrivateIdentifier<TEnvData, TPrivateEnvData, TPrivateEntry>(
        LexicalEnvironment<TEnvData, TPrivateEnvData, TPrivateEntry>? env,
        PrivateIdentifier name)
    {
        var entry = WalkUpLexicalEnvironments<TEnvData, TPrivateEnvData, TPrivateEntry, PrivateIdentifierEntry<TPrivateEntry>>(
            env,
            e => EntryOrNull(GetPrivateIdentifier(e.PrivateEnv, name)));
        return entry is null ? default : entry.Entry;
    }

    /// <summary>The wrapper <see cref="AccessPrivateIdentifier{TEnvData,TPrivateEnvData,TPrivateEntry}"/> needs to lift an
    /// entry into the walk's reference-typed result slot.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>walkUpLexicalEnvironments</c> is generic in <c>U</c> and its callback answers
    /// <c>TPrivateEntry | undefined</c>; the port's <c>U</c> needs a reference type for the <c>is not null</c> test that
    /// replaces TS's <c>result !== undefined</c>, so this one-field box is what carries the entry through the walk.
    /// </remarks>
    private sealed class PrivateIdentifierEntry<TEntry>(TEntry entry)
    {
        public TEntry Entry { get; } = entry;
    }

    private static PrivateIdentifierEntry<TEntry>? EntryOrNull<TEntry>(TEntry? entry)
        => entry is null ? null : new PrivateIdentifierEntry<TEntry>(entry);

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:866-886 — the parameter-list and module-specifier helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/utilities.ts:866-868 — <c>isSimpleParameter</c>.</summary>
    private static bool IsSimpleParameter(ParameterDeclaration node)
        => node.Initializer is null && NodeTests.IsIdentifier(node.Name!);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:871-873 — <c>isSimpleParameterList</c>.</summary>
    /// <remarks>PORT-NOTE: the landed <c>Core.Every</c> takes the index-taking callback, so the port states the reference's
    /// one-argument predicate with it.</remarks>
    public static bool IsSimpleParameterList(NodeArray<ParameterDeclaration> nodes)
        => Core.Every(nodes, (node, _) => IsSimpleParameter(node));

    /// <summary>ported from: src/compiler/transformers/utilities.ts:876-880 — <c>rewriteModuleSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS states three signatures — an <c>Expression</c>-taking one and two <c>Expression | undefined</c> ones,
    /// the last being the implementation; C# states one nullable-in/nullable-out member. The reference's
    /// <c>factory.createStringLiteral</c> is the module-level factory singleton
    /// (<see cref="NodeFactory.Factory"/>), which is what the two other helpers in this file that build nodes without a
    /// context use as well.
    /// </remarks>
    public static Expression? RewriteModuleSpecifier(Expression? node, ICompilerOptions compilerOptions)
    {
        if (node is null || !NodeTests.IsStringLiteral(node) || !Utilities.ShouldRewriteModuleSpecifier(((StringLiteral)node).Text!, compilerOptions))
        {
            return node;
        }

        var updatedText = Utilities.ChangeExtension(((StringLiteral)node).Text!, GetOutputExtensionForModuleSpecifier(((StringLiteral)node).Text!, compilerOptions));
        return updatedText != ((StringLiteral)node).Text
            ? NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(NodeFactory.Factory.CreateStringLiteral(updatedText, ((StringLiteral)node).SingleQuote), node),
                node)
            : node;
    }

    // ---------------------------------------------------------------------------------------------
    // Members stated here because they are this slice's first reader (their reference home has not landed,
    // or its only ported copy is private — see the class remarks)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:540-546 — <c>getOutputExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): the reference declares this in <c>emitter.ts</c> and the port's owner of that file is
    /// <c>Emitter.GetOutputExtension</c>, which landed in the M7-B slice; this reader calls it rather than stating a
    /// second copy (the "the reader declares it" copy that stood here was deleted when the home landed).
    /// </remarks>
    private static string GetOutputExtensionForModuleSpecifier(string fileName, ICompilerOptions options)
        => Emitter.GetOutputExtension(fileName, options);

    /// <summary>ported from: src/compiler/utilities.ts:4441-4443 — <c>isDefaultImport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>utilities.ts</c>'s members land on <c>Utilities</c>; this one has not landed and
    /// <c>transformers/utilities.ts</c> needs it (<c>getImportNeedsImportStarHelper</c>,
    /// <c>getImportNeedsImportDefaultHelper</c>), so it is stated here for its home class to take. TS's parameter is the
    /// union <c>ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration | JSDocImportTag</c> and its two kind
    /// tests are the single <c>ImportDeclaration</c> arm's <c>importClause</c> — the port takes
    /// <see cref="Node"/> for the union and answers the same way.
    /// </remarks>
    internal static bool IsDefaultImport(Node node)
        => node.Kind == SyntaxKind.ImportDeclaration
            && ((ImportDeclaration)node).ImportClause is { } importClause
            && importClause.Name is not null;

    /// <summary>ported from: src/compiler/utilities.ts:7603-7606 — <c>isEmptyArrayLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>utilities.ts</c>'s members land on <c>Utilities</c>; the only ported copy is private to
    /// <c>Checker</c> (<c>Checker.TypeOfSymbols.cs:438</c>), so <see cref="Destructuring"/>'s reader states this one
    /// here for its home class to take. Its pair <c>isEmptyObjectLiteral</c> is already landed as
    /// <see cref="Utilities.IsEmptyObjectLiteral"/> (Utilities/Utilities.Binder.Nodes.cs:593).
    /// </remarks>
    internal static bool IsEmptyArrayLiteral(Node expression)
        => expression.Kind == SyntaxKind.ArrayLiteralExpression
            && ((ArrayLiteralExpression)expression).Elements!.Count == 0;

    // ---------------------------------------------------------------------------------------------
    // utilitiesPublic.ts:1872-1945 — the six is*BindingOrAssignment* predicates, read by
    // transformers/destructuring.ts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1872-1877 — <c>isBindingOrAssignmentElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>utilitiesPublic.ts</c>'s members land on <c>Utilities</c>; this family has not landed and
    /// <c>transformers/destructuring.ts</c> reads all six. TS's results are the union guards
    /// (<c>BindingOrAssignmentElement</c>, <c>BindingOrAssignmentPattern</c>, <c>ObjectBindingOrAssignmentPattern</c>,
    /// <c>ArrayBindingOrAssignmentPattern</c>, <c>ObjectBindingOrAssignmentElement</c>,
    /// <c>ArrayBindingOrAssignmentElement</c>); the model declares no interface for any of them, so the port answers
    /// <c>bool</c> (mapping rule (d)) and every caller keeps reading <see cref="Node"/> — which is what the factory's
    /// binding-element helpers (Parser/FactoryUtilities.cs:612-860) already take.
    /// </remarks>
    internal static bool IsBindingOrAssignmentElement(Node node)
        => NodeTests.IsVariableDeclaration(node)
            || NodeTests.IsParameter(node)
            || IsObjectBindingOrAssignmentElement(node)
            || IsArrayBindingOrAssignmentElement(node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1884-1887 — <c>isBindingOrAssignmentPattern</c>.</summary>
    internal static bool IsBindingOrAssignmentPattern(Node node)
        => IsObjectBindingOrAssignmentPattern(node)
            || IsArrayBindingOrAssignmentPattern(node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1894-1902 — <c>isObjectBindingOrAssignmentPattern</c>.</summary>
    internal static bool IsObjectBindingOrAssignmentPattern(Node node)
        => node.Kind is SyntaxKind.ObjectBindingPattern or SyntaxKind.ObjectLiteralExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1905-1914 — <c>isObjectBindingOrAssignmentElement</c>.</summary>
    internal static bool IsObjectBindingOrAssignmentElement(Node node)
        => node.Kind is SyntaxKind.BindingElement or SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.SpreadAssignment;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1921-1929 — <c>isArrayBindingOrAssignmentPattern</c>.</summary>
    internal static bool IsArrayBindingOrAssignmentPattern(Node node)
        => node.Kind is SyntaxKind.ArrayBindingPattern or SyntaxKind.ArrayLiteralExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1932-1945 — <c>isArrayBindingOrAssignmentElement</c>.</summary>
    internal static bool IsArrayBindingOrAssignmentElement(Node node)
        => node.Kind is SyntaxKind.BindingElement
                or SyntaxKind.OmittedExpression // Elision
                or SyntaxKind.SpreadElement // AssignmentRestElement
                or SyntaxKind.ArrayLiteralExpression // ArrayAssignmentPattern
                or SyntaxKind.ObjectLiteralExpression // ObjectAssignmentPattern
                or SyntaxKind.Identifier // DestructuringAssignmentTarget
                or SyntaxKind.PropertyAccessExpression // DestructuringAssignmentTarget
                or SyntaxKind.ElementAccessExpression // DestructuringAssignmentTarget
            || Utilities.IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true); // AssignmentElement

    // ---------------------------------------------------------------------------------------------
    // factory/nodeFactory.ts and factory/utilities.ts — the two factory-side helpers
    // collectExternalModuleInfo reads
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6824-6826 — <c>getDeclarationName</c> (the
    /// <c>getName</c> delegate at 6759-6771).</summary>
    /// <remarks>
    /// PORT-NOTE (home): the reference declares this in <c>factory/nodeFactory.ts</c> as one member of the six-member
    /// <c>getName</c> family; the port's <see cref="NodeFactory"/> does not carry that family yet (docs/architecture.md
    /// §15.9 — only <c>GetGeneratedNameForNode</c>, <c>nodeFactory.ts:1377</c>, has landed, and this member calls it), so
    /// <c>collectExternalModuleInfo</c> — today's only reader — states the reference body here. It becomes a
    /// <see cref="NodeFactory"/> member when that family lands (the batch report names it).
    /// <para>
    /// The body is <c>getName</c>'s: the declaration's name when it is a plain identifier (cloned, with the original's
    /// emit flags plus <c>NoSourceMap</c>/<c>NoComments</c> — the reference's two <c>undefined</c> arguments), otherwise a
    /// generated name for the node. <c>getNameOfDeclaration</c> is <see cref="Utilities.GetNameOfDeclaration"/>.
    /// </para>
    /// </remarks>
    internal static Identifier GetDeclarationName(NodeFactory factory, Node node)
    {
        var nodeName = Utilities.GetNameOfDeclaration(node);
        if (nodeName is Identifier identifier && !Utilities.IsGeneratedIdentifier(identifier))
        {
            var name = Utilities.SetParent(Utilities.SetTextRange(factory.CloneNode(identifier), identifier), identifier.Parent);
            var emitFlags = Utilities.GetEmitFlags(identifier) | EmitFlags.NoSourceMap | EmitFlags.NoComments;
            return EmitNodes.SetEmitFlags(name!, emitFlags);
        }

        return factory.GetGeneratedNameForNode(node, /*flags*/ null, /*prefix*/ (object?)null, /*suffix*/ null);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:697-751 — <c>createExternalHelpersImportDeclarationIfNeeded</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): <c>FactoryUtilities</c> records this cluster in its header as left out — "they need
    /// <c>CompilerOptions</c>/<c>EmitHost</c>/<c>EmitResolver</c> and the module-kind helpers" — and every one of those has
    /// since landed (<c>Utilities.GetEmitModuleKind</c>, <c>Utilities.IsEffectiveExternalModule</c>,
    /// <c>Program.GetEmitModuleFormatOfFileWorker</c>, <c>Program.GetImpliedNodeFormatForEmitWorker</c>,
    /// <c>Utilities.IsFileLevelUniqueName</c>, <c>FactoryUtilities.GetExternalHelpersModuleName</c>).
    /// <c>collectExternalModuleInfo</c> is the cluster's first reader, so it is stated here; <c>FactoryUtilities</c> owns
    /// its home and the batch report names the move.
    /// <para>
    /// PORT-NOTE: TS's <c>sourceFile.emitNode</c> writes are the port's
    /// <c>EmitNodes.GetOrCreateEmitNode</c> reads; <c>helperNames.sort(compareStringsCaseSensitive)</c> is
    /// <c>Core.StableSort</c> (TS's <c>Array.prototype.sort</c> is the stable one, §10.2).
    /// </para>
    /// </remarks>
    internal static Node? CreateExternalHelpersImportDeclarationIfNeeded(
        NodeFactory factory,
        IEmitHelperFactory helperFactory,
        SourceFile sourceFile,
        ICompilerOptions compilerOptions,
        bool? hasExportStarsToExportValues,
        bool? hasImportStar,
        bool? hasImportDefault)
    {
        if (Js.IsTruthy(compilerOptions.ImportHelpers) && Utilities.IsEffectiveExternalModule(sourceFile, compilerOptions))
        {
            var moduleKind = Utilities.GetEmitModuleKind(compilerOptions);
            var impliedModuleKind = Program.GetImpliedNodeFormatForEmitWorker(sourceFile, compilerOptions);
            var helpers = GetImportedHelpers(sourceFile);
            if (
                impliedModuleKind != ModuleKind.CommonJS
                && ((moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext)
                    || impliedModuleKind == ModuleKind.ESNext
                    || impliedModuleKind is null && moduleKind == ModuleKind.Preserve)
            )
            {
                // When we emit as an ES module, generate an `import` declaration that uses named imports for helpers.
                // If we cannot determine the implied module kind under `module: preserve` we assume ESM.
                if (helpers is not null)
                {
                    var helperNames = new List<string>();
                    foreach (var helper in helpers)
                    {
                        var importName = ((IUnscopedEmitHelper)helper!).ImportName;
                        if (importName is not null)
                        {
                            Core.PushIfUnique(helperNames, importName);
                        }
                    }

                    if (Core.Some(helperNames))
                    {
                        Core.StableSort(helperNames, (a, b) => Core.CompareStringsCaseSensitive(a, b));
                        // Alias the imports if the names are used somewhere in the file.
                        // NOTE: We don't need to care about global import collisions as this is a module.
                        var namedBindings = factory.CreateNamedImports(
                            Core.Map(helperNames, (name, _) =>
                                Utilities.IsFileLevelUniqueName(sourceFile, name)
                                    ? factory.CreateImportSpecifier(/*isTypeOnly*/ false, /*propertyName*/ null, factory.CreateIdentifier(name, null, null))
                                    : factory.CreateImportSpecifier(/*isTypeOnly*/ false, factory.CreateIdentifier(name, null, null), helperFactory.GetUnscopedHelperName(name)))!)!;
                        var parseNode = Utilities.GetOriginalNode(sourceFile, NodeTests.IsSourceFile)!;
                        var emitNode = EmitNodes.GetOrCreateEmitNode(parseNode);
                        emitNode.ExternalHelpers = true;

                        var externalHelpersImportDeclaration = factory.CreateImportDeclaration(
                            /*modifiers*/ null,
                            factory.CreateImportClause(/*phaseModifier*/ null, /*name*/ null, namedBindings),
                            factory.CreateStringLiteral(Utilities.ExternalHelpersModuleNameText, null, null),
                            /*attributes*/ null);
                        EmitNodes.AddInternalEmitFlags(externalHelpersImportDeclaration, InternalEmitFlags.NeverApplyImportHelper);
                        return externalHelpersImportDeclaration;
                    }
                }
            }
            else
            {
                // When we emit to a non-ES module, generate a synthetic `import tslib = require("tslib")` to be further
                // transformed.
                var externalHelpersModuleName = GetOrCreateExternalHelpersModuleNameIfNeeded(
                    factory,
                    sourceFile,
                    compilerOptions,
                    helpers,
                    hasExportStarsToExportValues,
                    Js.IsTruthy(hasImportStar) || Js.IsTruthy(hasImportDefault));
                if (externalHelpersModuleName is not null)
                {
                    var externalHelpersImportDeclaration = factory.CreateImportEqualsDeclaration(
                        /*modifiers*/ null,
                        /*isTypeOnly*/ false,
                        externalHelpersModuleName,
                        factory.CreateExternalModuleReference(factory.CreateStringLiteral(Utilities.ExternalHelpersModuleNameText, null, null)));
                    EmitNodes.AddInternalEmitFlags(externalHelpersImportDeclaration, InternalEmitFlags.NeverApplyImportHelper);
                    return externalHelpersImportDeclaration;
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:760-762 — <c>getImportedHelpers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>helper.scoped</c> is <see cref="IEmitHelperBase.Scoped"/>; the port's helper lists are
    /// <c>List&lt;Node&gt;</c> (an <c>EmitHelper</c> descriptor is a <see cref="Node"/>, Model/EmitHelper.cs:47), so the
    /// read is the interface cast the emitter makes for the same slot.
    /// </remarks>
    private static IList<Node>? GetImportedHelpers(SourceFile sourceFile)
        => Core.Filter(EmitNodes.GetEmitHelpers(sourceFile), helper => !((IEmitHelperBase)helper!).Scoped);

    /// <summary>ported from: src/compiler/factory/utilities.ts:764-778 — <c>getOrCreateExternalHelpersModuleNameIfNeeded</c>.</summary>
    private static Identifier? GetOrCreateExternalHelpersModuleNameIfNeeded(
        NodeFactory factory,
        SourceFile node,
        ICompilerOptions compilerOptions,
        IList<Node>? helpers,
        bool? hasExportStarsToExportValues,
        bool hasImportStarOrImportDefault)
    {
        var externalHelpersModuleName = FactoryUtilities.GetExternalHelpersModuleName(node);
        if (externalHelpersModuleName is not null)
        {
            return externalHelpersModuleName;
        }

        var create = Core.Some(helpers)
            || (Js.IsTruthy(hasExportStarsToExportValues) || (Utilities.GetESModuleInterop(compilerOptions) && hasImportStarOrImportDefault))
                && Program.GetEmitModuleFormatOfFileWorker(node, compilerOptions) < ModuleKind.System;

        if (create)
        {
            var parseNode = Utilities.GetOriginalNode(node, NodeTests.IsSourceFile)!;
            var emitNode = EmitNodes.GetOrCreateEmitNode(parseNode);
            return emitNode.ExternalHelpersModuleName ??= factory.CreateUniqueName(Utilities.ExternalHelpersModuleNameText, /*flags*/ null);
        }

        return null;
    }
}

/// <summary>ported from: src/compiler/types.ts:9552-9607 — the slice of <c>TransformationContext</c> that
/// <c>transformers/utilities.ts</c>, <c>transformers/destructuring.ts</c> and <c>transformers/taggedTemplate.ts</c>
/// read.</summary>
/// <remarks>
/// PORT-NOTE (the context face): like <see cref="IVisitorTransformationContext"/> and
/// <see cref="IEmitHelperFactoryContext"/> before it, this is the port's slice of the reference's
/// <c>TransformationContext</c>. The generated <see cref="ITransformationContext"/> cannot be implemented by any type in
/// the port — <c>ICoreTransformationContext.Factory</c> is <c>INodeFactory?</c> and the port's
/// <see cref="NodeFactory"/> does not implement <see cref="INodeFactory"/> (docs/architecture.md §15.9) — and the
/// reference reads the concrete factory here (<c>context.factory.createTempVariable</c>,
/// <c>context.factory.createElementAccessExpression</c>, …), so the factory slot is the concrete
/// <see cref="NodeFactory"/> for the same reason those two interfaces state it.
/// <para>
/// It extends <see cref="IVisitorTransformationContext"/> rather than redeclaring the factory and options members,
/// because <c>transformers/taggedTemplate.ts</c> is also a caller of <c>visitEachChild</c> — the one member of the
/// visitor layer that reads a context — and that interface is already its port home. The three members below are what
/// these files read beyond it.
/// </para>
/// </remarks>
internal interface ITransformationUtilitiesContext : IVisitorTransformationContext
{
    /// <summary>ported from: src/compiler/types.ts:9573 — <c>CoreTransformationContext.hoistVariableDeclaration</c>.</summary>
    void HoistVariableDeclaration(Identifier? node);

    /// <summary>ported from: src/compiler/types.ts:9585 — <c>TransformationContext.getEmitResolver</c>.</summary>
    IEmitResolver GetEmitResolver();

    /// <summary>ported from: src/compiler/types.ts:9587 — <c>TransformationContext.getEmitHelperFactory</c>.</summary>
    IEmitHelperFactory GetEmitHelperFactory();
}