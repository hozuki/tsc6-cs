namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2336-2339 (hasExportDeclarations)
// ported from: src/compiler/binder.ts:2341-2350 (setExportContextFlag)
// ported from: src/compiler/binder.ts:2352-2387 (bindModuleDeclaration)
// ported from: src/compiler/binder.ts:2389-2398 (declareModuleSymbol)
// ported from: src/compiler/binder.ts:2400-2414 (bindFunctionOrConstructorType)
// ported from: src/compiler/binder.ts:2416-2418 (bindObjectLiteralExpression)
// ported from: src/compiler/binder.ts:2420-2422 (bindJsxAttributes)
// ported from: src/compiler/binder.ts:2424-2426 (bindJsxAttribute)
// ported from: src/compiler/binder.ts:3108-3120 (bindSourceFileIfExternalModule)
// ported from: src/compiler/binder.ts:3122-3124 (bindSourceFileAsExternalModule)
// ported from: src/compiler/binder.ts:3126-3146 (bindExportAssignment)
// ported from: src/compiler/binder.ts:3148-3163 (bindNamespaceExportDeclaration)
// ported from: src/compiler/binder.ts:3165-3180 (bindExportDeclaration)
// ported from: src/compiler/binder.ts:3182-3186 (bindImportClause)
// ported from: src/compiler/binder.ts:3188-3199 (setCommonJsModuleIndicator)
/// <summary>
/// The module and export half of the binder's walk: the source file's external-module decision, the module
/// declaration, the import/export declarations, and the CommonJS indicators — TS's
/// <c>bindSourceFileIfExternalModule</c>…<c>setCommonJsModuleIndicator</c> plus the module/JSX/anonymous-type
/// members that sit beside <c>bindAnonymousDeclaration</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: every member here is the binder's walk rather than this slice's own — <c>bindWorker</c>'s dispatch
/// (M5-5) is the only caller of most of them, and the JSDoc deferral set (M5-3) calls
/// <c>setExportContextFlag</c>'s neighbours — so they are <c>internal</c> like the state slots (Binder.State.cs's
/// remarks give the same reason). Only the two helpers no other slice names stay <c>private</c>.
/// <para>
/// PORT-NOTE: TS reads <c>container.symbol</c>/<c>container.symbol.exports</c> off its <c>IsContainer</c>-typed
/// <c>container</c>; the port's slot is a plain <see cref="Node"/> (§12.9), so each read goes through
/// <see cref="IDeclaration.Symbol"/>. TS's <c>!container.symbol || !container.symbol.exports</c> is one property
/// pattern here, because a null intermediate simply fails to match.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2336-2339 — <c>hasExportDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ModuleDeclaration | SourceFile</c> union and its
    /// <c>tryCast(node.body, isModuleBlock)</c> picks the module-block arm; both arms are one pattern switch here. The
    /// statement list is <c>NodeArray&lt;IStatement&gt;</c> — the port's element type for statement arrays — so the
    /// elements are cast to <see cref="Node"/>, the same cast
    /// <c>getModuleInstanceStateForAliasTarget</c> makes (§12.9).
    /// </remarks>
    internal static bool HasExportDeclarations(Node node)
    {
        var statements = node switch
        {
            SourceFile sourceFile => sourceFile.Statements,
            ModuleDeclaration { Body: ModuleBlock body } => body.Statements,
            _ => null,
        };

        return statements is not null
            && Core.Some(statements, s => NodeTests.IsExportDeclaration((Node)s) || NodeTests.IsExportAssignment((Node)s));
    }

    /// <summary>ported from: src/compiler/binder.ts:2341-2350 — <c>setExportContextFlag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Mutable&lt;ModuleDeclaration | SourceFile&gt;</c>; <see cref="Node.Flags"/> is
    /// a mutable slot on the port's base class, so the honest C# bound is <see cref="Node"/> and the two writes are
    /// the same read-modify-writes.
    /// </remarks>
    internal static void SetExportContextFlag(Node node)
    {
        // A declaration source file or ambient module declaration that contains no export declarations (but possibly regular
        // declarations with export modifiers) is an export context in which declarations are implicitly exported.
        if ((node.Flags & NodeFlags.Ambient) != 0 && !HasExportDeclarations(node))
        {
            node.Flags |= NodeFlags.ExportContext;
        }
        else
        {
            node.Flags &= ~NodeFlags.ExportContext;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2352-2387 — <c>bindModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.name.kind === SyntaxKind.StringLiteral</c> followed by <c>const { text } = node.name</c>
    /// becomes a <see cref="StringLiteral"/> pattern that binds the literal, and the literal's <c>Text</c> is asserted
    /// with <see cref="Debug.CheckDefined{T}(T?, string?)"/> because the port types that slot nullable (§12.6).
    /// <para>
    /// PORT-NOTE: <c>errorOnFirstToken</c> (binder.ts:2746-2749) is the strict-mode/JSDoc slice's (M5-3); it is named
    /// here as <c>ErrorOnFirstToken(Node node, DiagnosticMessage message, params object?[] args)</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>tryParsePattern</c> (utilities.ts:10203-10217) is not part of the M5-0 utilities slice; the port
    /// states it in Utilities/Utilities.Binder.Modules.cs as the .NET three-state shape
    /// <c>TryParsePattern(string text, out Core.Pattern? pattern)</c> — answering <c>false</c> for TS's
    /// <c>undefined</c> and leaving <c>pattern</c> null for TS's plain-string answer — and TS's <c>append</c> becomes
    /// <see cref="Core.Append{T}"/> over the <see cref="PatternAmbientModule"/> the pointer/file pair builds.
    /// </para>
    /// </remarks>
    internal void BindModuleDeclaration(ModuleDeclaration node)
    {
        SetExportContextFlag(node);

        if (Utilities.IsAmbientModule(node))
        {
            if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
            {
                ErrorOnFirstToken(node, Diagnostics.export_modifier_cannot_be_applied_to_ambient_modules_and_module_augmentations_since_they_are_always_visible);
            }

            if (Utilities.IsModuleAugmentationExternal(node))
            {
                DeclareModuleSymbol(node);
            }
            else
            {
                Core.Pattern? pattern = null;
                var patternIsValid = true;
                if (node.Name is StringLiteral stringLiteral)
                {
                    var text = Debug.CheckDefined(stringLiteral.Text, "a string module name must have text");
                    patternIsValid = Utilities.TryParsePattern(text, out pattern);
                    if (!patternIsValid)
                    {
                        ErrorOnFirstToken(node.Name, Diagnostics.Pattern_0_can_have_at_most_one_Asterisk_character, text);
                    }
                }

                var symbol = DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.ValueModule, SymbolFlags.ValueModuleExcludes)!;
                _file.PatternAmbientModules = Core.Append<IPatternAmbientModule>(
                        _file.PatternAmbientModules,
                        patternIsValid && pattern is not null ? new PatternAmbientModule(pattern.Value, symbol) : null)
                    as List<IPatternAmbientModule>;
            }
        }
        else
        {
            var state = DeclareModuleSymbol(node);
            if (state != ModuleInstanceState.NonInstantiated)
            {
                var symbol = node.Symbol!;
                // if module was already merged with some function, class or non-const enum, treat it as non-const-enum-only
                symbol.ConstEnumOnlyModule = (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) == 0
                    // Current must be `const enum` only
                    && state == ModuleInstanceState.ConstEnumOnly
                    // Can't have been set to 'false' in a previous merged symbol. ('undefined' OK)
                    && symbol.ConstEnumOnlyModule != false;
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2389-2398 — <c>declareModuleSymbol</c>.</summary>
    internal ModuleInstanceState DeclareModuleSymbol(ModuleDeclaration node)
    {
        var state = GetModuleInstanceState(node);
        var instantiated = state != ModuleInstanceState.NonInstantiated;
        DeclareSymbolAndAddToSymbolTable(
            node,
            instantiated ? SymbolFlags.ValueModule : SymbolFlags.NamespaceModule,
            instantiated ? SymbolFlags.ValueModuleExcludes : SymbolFlags.NamespaceModuleExcludes);
        return state;
    }

    /// <summary>ported from: src/compiler/binder.ts:2400-2414 — <c>bindFunctionOrConstructorType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>SignatureDeclaration | JSDocSignature</c> union; the port takes
    /// <see cref="IDeclaration"/>, the one capability face all six kinds that reach this member
    /// (<c>FunctionType</c>, <c>ConstructorType</c>, <c>CallSignature</c>, <c>ConstructSignature</c>,
    /// <c>JSDocSignature</c>, <c>JSDocFunctionType</c>) declare, and the face this body needs.
    /// <para>
    /// PORT-NOTE: <c>getDeclarationName(node)!</c> carries TS's <c>GH#18217</c> note; the port states the same
    /// invariant with <see cref="Debug.CheckDefined{T}(T?, string?)"/>, as <c>getDisplayName</c> does.
    /// </para>
    /// </remarks>
    internal void BindFunctionOrConstructorType(IDeclaration node)
    {
        // For a given function symbol "<...>(...) => T" we want to generate a symbol identical
        // to the one we would get for: { <...>(...): T }
        //
        // We do that by making an anonymous type literal symbol, and then setting the function
        // symbol as its sole member. To the rest of the system, this symbol will be indistinguishable
        // from an actual type literal symbol you would have gotten had you used the long form.
        var symbol = CreateSymbol(SymbolFlags.Signature, Debug.CheckDefined(GetDeclarationName(node), "a function or constructor type must have a declaration name"));
        AddDeclarationToSymbol(symbol, node, SymbolFlags.Signature);

        var typeLiteralSymbol = CreateSymbol(SymbolFlags.TypeLiteral, InternalSymbolName.Type.Value);
        AddDeclarationToSymbol(typeLiteralSymbol, node, SymbolFlags.TypeLiteral);
        typeLiteralSymbol.Members = Utilities.CreateSymbolTable();
        typeLiteralSymbol.Members[symbol.EscapedName!] = symbol;
    }

    /// <summary>ported from: src/compiler/binder.ts:2416-2418 — <c>bindObjectLiteralExpression</c>.</summary>
    internal ISymbol BindObjectLiteralExpression(ObjectLiteralExpression node)
        => BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, InternalSymbolName.Object.Value);

    /// <summary>ported from: src/compiler/binder.ts:2420-2422 — <c>bindJsxAttributes</c>.</summary>
    internal ISymbol BindJsxAttributes(JsxAttributes node)
        => BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, InternalSymbolName.JSXAttributes.Value);

    /// <summary>ported from: src/compiler/binder.ts:2424-2426 — <c>bindJsxAttribute</c>.</summary>
    internal ISymbol? BindJsxAttribute(JsxAttribute node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        => DeclareSymbolAndAddToSymbolTable(node, symbolFlags, symbolExcludes);

    /// <summary>ported from: src/compiler/binder.ts:3108-3120 — <c>bindSourceFileIfExternalModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isExternalModule</c> is the parser's one-liner (<c>Parser.IsExternalModule</c>), while
    /// <c>isJsonSourceFile</c> is <see cref="Utilities.IsJsonSourceFile"/> — the same two members TS picks.
    /// </remarks>
    internal void BindSourceFileIfExternalModule()
    {
        SetExportContextFlag(_file);
        if (Parser.IsExternalModule(_file))
        {
            BindSourceFileAsExternalModule();
        }
        else if (Utilities.IsJsonSourceFile(_file))
        {
            BindSourceFileAsExternalModule();
            // Create symbol equivalent for the module.exports = {}
            var originalSymbol = _file.Symbol;
            DeclareSymbol(_file.Symbol!.Exports!, _file.Symbol, _file, SymbolFlags.Property, SymbolFlags.All);
            _file.Symbol = originalSymbol;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3122-3124 — <c>bindSourceFileAsExternalModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: both templated operands are nullable slots in the port (<c>fileName</c>) or a plain
    /// <see cref="string"/> where TS interpolated a <c>__String</c>; the port asserts the file name rather than
    /// interpolating a null.
    /// </remarks>
    internal void BindSourceFileAsExternalModule()
        => BindAnonymousDeclaration(_file, SymbolFlags.ValueModule, $"\"{Utilities.RemoveFileExtension(_file.FileName!)}\"");

    /// <summary>ported from: src/compiler/binder.ts:3126-3146 — <c>bindExportAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.isExportEquals</c> is <c>bool?</c> in the model, so the test is
    /// <see cref="Js.IsTruthy(object?)"/> — the same truthiness test TS applies.
    /// </remarks>
    internal void BindExportAssignment(ExportAssignment node)
    {
        if (_container is not IDeclaration { Symbol.Exports: not null } containerDeclaration)
        {
            // Incorrect export assignment in some sort of block construct
            BindAnonymousDeclaration(node, SymbolFlags.Value, Debug.CheckDefined(GetDeclarationName(node), "an export assignment must have a declaration name"));
        }
        else
        {
            var flags = Utilities.ExportAssignmentIsAlias(node)
                // An export default clause with an EntityNameExpression or a class expression exports all meanings of that identifier or expression;
                ? SymbolFlags.Alias
                // An export default clause with any other expression exports a value
                : SymbolFlags.Property;
            // If there is an `export default x;` alias declaration, can't `export default` anything else.
            // (In contrast, you can still have `export default function f() {}` and `export default interface I {}`.)
            var symbol = DeclareSymbol(containerDeclaration.Symbol!.Exports!, containerDeclaration.Symbol, node, flags, SymbolFlags.All);

            if (Js.IsTruthy(node.IsExportEquals))
            {
                // Will be an error later, since the module already has other exports. Just make sure this has a valueDeclaration set.
                Utilities.SetValueDeclaration(symbol, node);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3148-3163 — <c>bindNamespaceExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>diag</c> is a four-armed <c>?:</c> chain whose last arm is <c>undefined</c>; the port states
    /// the same chain over a nullable <see cref="DiagnosticMessage"/> and tests it once. A node with no parent fails
    /// the first arm exactly as TS's <c>!isSourceFile(undefined)</c> does.
    /// </remarks>
    internal void BindNamespaceExportDeclaration(NamespaceExportDeclaration node)
    {
        if (Core.Some(node.Modifiers))
        {
            _file.BindDiagnostics!.Add(CreateDiagnosticForNode(node, Diagnostics.Modifiers_cannot_appear_here));
        }

        var diag = node.Parent is not SourceFile parent ? Diagnostics.Global_module_exports_may_only_appear_at_top_level
            : !Parser.IsExternalModule(parent) ? Diagnostics.Global_module_exports_may_only_appear_in_module_files
            : !parent.IsDeclarationFile ? Diagnostics.Global_module_exports_may_only_appear_in_declaration_files
            : null;
        if (diag is not null)
        {
            _file.BindDiagnostics!.Add(CreateDiagnosticForNode(node, diag));
        }
        else
        {
            _file.Symbol!.GlobalExports ??= Utilities.CreateSymbolTable();
            DeclareSymbol(_file.Symbol.GlobalExports, _file.Symbol, node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3165-3180 — <c>bindExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.exportClause</c> is the <c>NamedExports | NamespaceExport</c> union and a <c>Node?</c> slot
    /// here; the <c>isNamespaceExport</c> guard is what makes the cast to the declaration face total, and
    /// <c>setParent(node.exportClause, node)</c> is generic over the child (Utilities.SetParent).
    /// </remarks>
    internal void BindExportDeclaration(ExportDeclaration node)
    {
        if (_container is not IDeclaration { Symbol.Exports: not null } containerDeclaration)
        {
            // Export * in some sort of block construct
            BindAnonymousDeclaration(node, SymbolFlags.ExportStar, Debug.CheckDefined(GetDeclarationName(node), "an export declaration must have a declaration name"));
        }
        else if (node.ExportClause is null)
        {
            // All export * declarations are collected in an __export symbol
            DeclareSymbol(containerDeclaration.Symbol!.Exports!, containerDeclaration.Symbol, node, SymbolFlags.ExportStar, SymbolFlags.None);
        }
        else if (NodeTests.IsNamespaceExport(node.ExportClause))
        {
            // declareSymbol walks up parents to find name text, parent _must_ be set
            // but won't be set by the normal binder walk until `bindChildren` later on.
            Utilities.SetParent(node.ExportClause, node);
            DeclareSymbol(containerDeclaration.Symbol!.Exports!, containerDeclaration.Symbol, (IDeclaration)node.ExportClause, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3182-3186 — <c>bindImportClause</c>.</summary>
    internal void BindImportClause(ImportClause node)
    {
        if (node.Name is not null)
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3188-3199 — <c>setCommonJsModuleIndicator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>file.externalModuleIndicator</c> is <c>Node | true</c> upstream and <c>object?</c> in the model
    /// (Model/NodeClasses.cs:2122), so <c>externalModuleIndicator !== true</c> is the pattern test
    /// <c>is not true</c> and the two truthiness tests are <see cref="Js.IsTruthy(object?)"/>.
    /// </remarks>
    internal bool SetCommonJsModuleIndicator(Node node)
    {
        if (Js.IsTruthy(_file.ExternalModuleIndicator) && _file.ExternalModuleIndicator is not true)
        {
            return false;
        }

        if (_file.CommonJsModuleIndicator is null)
        {
            _file.CommonJsModuleIndicator = node;
            if (!Js.IsTruthy(_file.ExternalModuleIndicator))
            {
                BindSourceFileAsExternalModule();
            }
        }

        return true;
    }
}
