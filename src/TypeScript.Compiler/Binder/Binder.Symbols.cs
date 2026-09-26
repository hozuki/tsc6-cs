namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:630-633 (createSymbol)
// ported from: src/compiler/binder.ts:635-659 (addDeclarationToSymbol)
// ported from: src/compiler/binder.ts:661-735 (getDeclarationName)
// ported from: src/compiler/binder.ts:737-747 (getDisplayName)
// ported from: src/compiler/binder.ts:749-885 (declareSymbol)
/// <summary>
/// The binder's symbol factory: the four members every declaration goes through on its way into a symbol table —
/// TS's <c>createSymbol</c>, <c>addDeclarationToSymbol</c>, <c>getDeclarationName</c>, <c>getDisplayName</c> and the
/// <c>declareSymbol</c> that drives them.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's parameters of type <c>Declaration</c> become <see cref="IDeclaration"/>, the port's capability face
/// for the two slots these members write (<c>symbol</c>/<c>localSymbol</c>, §12.9). The reads that live on the *class*
/// — <c>parent</c>, <c>name</c> — are taken through a <c>Node</c> local, because <c>INode</c> carries no parent pointer
/// (Model/Node.cs:20-26) and <c>INamedDeclaration.Name</c> is get-only.
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:630-633 — <c>createSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds a symbol with <c>new Symbol(flags, name)</c> where <c>Symbol</c> is the constructor
    /// <c>objectAllocator.getSymbolConstructor()</c> returned at binder.ts:579. Neither the allocator (§15.2) nor the
    /// slot exists here, so the port constructs <see cref="Symbol"/> directly.
    /// </remarks>
    internal ISymbol CreateSymbol(SymbolFlags flags, string name)
    {
        _symbolCount++;
        return new Symbol(flags, name);
    }

    /// <summary>ported from: src/compiler/binder.ts:635-659 — <c>addDeclarationToSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>appendIfUnique</c> is <see cref="Core.AppendIfUnique{T}"/>, which answers the same
    /// <c>IList</c> it was given (a new list only when the slot was empty); the cast to the slot's
    /// <c>List&lt;IDeclaration&gt;</c> mirrors the precedent at Parser/EmitNodes.cs:310.
    /// <para>
    /// PORT-NOTE: the four <c>symbolFlags &amp; (...)</c> tests are C# flag-set tests (<c>!= 0</c>); TS relies on
    /// numbers being truthy.
    /// </para>
    /// </remarks>
    internal void AddDeclarationToSymbol(ISymbol symbol, IDeclaration node, SymbolFlags symbolFlags)
    {
        symbol.Flags |= symbolFlags;

        node.Symbol = symbol;
        symbol.Declarations = Core.AppendIfUnique(symbol.Declarations, node) as List<IDeclaration>;

        if ((symbolFlags & (SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.Module | SymbolFlags.Variable)) != 0 && symbol.Exports is null)
        {
            symbol.Exports = Utilities.CreateSymbolTable();
        }

        if ((symbolFlags & (SymbolFlags.Class | SymbolFlags.Interface | SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) != 0 && symbol.Members is null)
        {
            symbol.Members = Utilities.CreateSymbolTable();
        }

        // On merge of const enum module with class or function, reset const enum only flag (namespaces will already recalculate)
        if (symbol.ConstEnumOnlyModule == true && (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) != 0)
        {
            symbol.ConstEnumOnlyModule = false;
        }

        if ((symbolFlags & SymbolFlags.Value) != 0)
        {
            Utilities.SetValueDeclaration(symbol, node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:661-735 — <c>getDeclarationName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the answer <c>__String | undefined</c>; the port's escaped names are plain
    /// <c>string</c> (§5.1), so <c>null</c> is the absent answer.
    /// <para>
    /// PORT-NOTE: <c>getNameOfDeclaration</c> answers <c>DeclarationName | undefined</c> in TS and <c>object?</c>
    /// here (the union has no C# anchor); every arm below tests the node's kind, so the value is cast to
    /// <see cref="Node"/> once and read by kind from there.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS reads <c>nameExpression.text</c> in two arms. The port reads <c>Text</c> off
    /// <see cref="LiteralExpression"/>, the class a string/numeric/no-substitution-template literal shares. The
    /// <c>isSignedNumericLiteral</c> arm casts to <see cref="PrefixUnaryExpression"/> and then to
    /// <see cref="NumericLiteral"/> for the operand: TS's own guard also admits a bare numeric literal, but that case
    /// is already answered by the <c>isStringOrNumericLiteralLike</c> arm directly above, so it is unreachable here —
    /// and the port's <c>IsSignedNumericLiteral</c> (Utilities.JsExpando.cs:647) admits only the prefix form anyway.
    /// </para>
    /// <para>
    /// PORT-NOTE: the final <c>case SyntaxKind.Parameter</c> asserts with <c>Debug.Assert</c> and reads the parent's
    /// parameter list; the port's signature-declaration face declares <c>Parameters</c>, and <c>NodeArray&lt;T&gt;</c>
    /// is a <c>List&lt;T&gt;</c>, so <c>IndexOf</c> is the same call TS makes.
    /// </para>
    /// </remarks>
    internal string? GetDeclarationName(IDeclaration declaration)
    {
        var node = (Node)declaration;
        if (node.Kind == SyntaxKind.ExportAssignment)
        {
            return Js.IsTruthy(((ExportAssignment)node).IsExportEquals)
                ? InternalSymbolName.ExportEquals.Value
                : InternalSymbolName.Default.Value;
        }

        var name = (Node?)Utilities.GetNameOfDeclaration((Node)declaration);
        if (name is not null)
        {
            if (Utilities.IsAmbientModule(node))
            {
                var moduleName = Utilities.GetTextOfIdentifierOrLiteral(name);
                return Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)node)
                    ? InternalSymbolName.Global.Value
                    : $"\"{moduleName}\"";
            }

            if (name.Kind == SyntaxKind.ComputedPropertyName)
            {
                var nameExpression = ((ComputedPropertyName)name).Expression;
                // treat computed property names where expression is string/numeric literal as just string/numeric literal
                if (nameExpression is not null && Utilities.IsStringOrNumericLiteralLike(nameExpression))
                {
                    return Utilities.EscapeLeadingUnderscores(((LiteralExpression)nameExpression).Text ?? "");
                }

                if (nameExpression is not null && Utilities.IsSignedNumericLiteral(nameExpression))
                {
                    var prefixUnary = (PrefixUnaryExpression)nameExpression;
                    return ScannerUtilities.TokenToString(prefixUnary.Operator) + ((NumericLiteral)prefixUnary.Operand!).Text;
                }
                else
                {
                    Debug.Fail("Only computed properties with literal names have declaration names");
                }
            }

            if (NodeTests.IsPrivateIdentifier(name))
            {
                // containingClass exists because private names only allowed inside classes
                var containingClass = Utilities.GetContainingClass(node);
                if (containingClass is null)
                {
                    // we can get here in cases where there is already a parse error.
                    return null;
                }

                var containingClassSymbol = ((IDeclaration)containingClass).Symbol;
                return Utilities.GetSymbolNameForPrivateIdentifier(containingClassSymbol!, ((PrivateIdentifier)name).EscapedText ?? "");
            }

            if (NodeTests.IsJsxNamespacedName(name))
            {
                return Utilities.GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)name);
            }

            return Utilities.IsPropertyNameLiteral(name) ? Utilities.GetEscapedTextOfIdentifierOrLiteral(name) : null;
        }

        switch (node.Kind)
        {
            case SyntaxKind.Constructor:
                return InternalSymbolName.Constructor.Value;
            case SyntaxKind.FunctionType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.JSDocSignature:
                return InternalSymbolName.Call.Value;
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
                return InternalSymbolName.New.Value;
            case SyntaxKind.IndexSignature:
                return InternalSymbolName.Index.Value;
            case SyntaxKind.ExportDeclaration:
                return InternalSymbolName.ExportStar.Value;
            case SyntaxKind.SourceFile:
                // json file should behave as
                // module.exports = ...
                return InternalSymbolName.ExportEquals.Value;
            case SyntaxKind.BinaryExpression:
                if (Utilities.GetAssignmentDeclarationKind(node) == AssignmentDeclarationKind.ModuleExports)
                {
                    // module.exports = ...
                    return InternalSymbolName.ExportEquals.Value;
                }

                Debug.Fail("Unknown binary declaration kind");
                break;
            case SyntaxKind.JSDocFunctionType:
                return Utilities.IsJSDocConstructSignature(node)
                    ? InternalSymbolName.New.Value
                    : InternalSymbolName.Call.Value;
            case SyntaxKind.Parameter:
            {
                // Parameters with names are handled at the top of this function.  Parameters
                // without names can only come from JSDocFunctionTypes.
                Debug.Assert(
                    node.Parent!.Kind == SyntaxKind.JSDocFunctionType,
                    "Impossible parameter parent kind",
                    () => $"parent is: {Debug.FormatSyntaxKind(node.Parent!.Kind)}, expected JSDocFunctionType");
                var functionType = (JSDocFunctionType)node.Parent!;
                var index = functionType.Parameters!.IndexOf((ParameterDeclaration)node);
                return "arg" + index;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:737-747 — <c>getDisplayName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isNamedDeclaration(node) ? declarationNameToString(node.name) : …</c> reads the duck-typed
    /// name slot; the port takes that slot once through <see cref="Utilities.GetNamedDeclarationName"/>, which is what
    /// keeps a JSX attribute (a declaration whose name the model does not mark <see cref="INamedDeclaration"/>) out of
    /// the fallback and out of a cast that would throw (§12.9). <c>unescapeLeadingUnderscores</c> is applied to
    /// <c>checkDefined(getDeclarationName(node))</c> — a declaration that reaches the fallback always has one, and the
    /// port says so with <see cref="Debug.CheckDefined{T}(T?, string?)"/> rather than the null-forgiving operator, so a
    /// factory bug fails loudly.
    /// </remarks>
    internal string GetDisplayName(IDeclaration node)
    {
        var name = Utilities.GetNamedDeclarationName((Node)node);
        return name is not null
            ? Utilities.DeclarationNameToString(name)
            : Utilities.UnescapeLeadingUnderscores(Debug.CheckDefined(GetDeclarationName(node), "a declaration reported by getDisplayName must have a name"));
    }

    /// <summary>ported from: src/compiler/binder.ts:749-885 — <c>declareSymbol</c>.</summary>
    /// <remarks>
    /// Declares a Symbol for the node and adds it to symbols. Reports errors for conflicting identifier names.
    /// <para>
    /// PORT-NOTE: TS's optional <c>isReplaceableByMethod?</c>/<c>isComputedName?</c> are <c>true | undefined</c>; the
    /// port spells both <c>bool?</c> and tests them with <c>Js.IsTruthy</c> where TS tests truthiness.
    /// </para>
    /// <para>
    /// PORT-NOTE: the two diagnostic fan-outs use <see cref="Core.ForEach{T, TU}"/>, whose callback answers a value.
    /// TS's callback answers <c>void</c>, so the port's returns <c>(object?)null</c> — the same "keep walking" the
    /// empty-answer test reads everywhere else.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's inner <c>symbol.declarations &amp;&amp; symbol.declarations.length</c> test is redundant inside
    /// the <c>length(symbol.declarations)</c> branch it sits in; the port states the outer test once.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>file.bindDiagnostics.push</c> assumes the array created by <c>createSourceFile</c>
    /// (Parser/Parser.cs:302), so the port's <c>_file.BindDiagnostics!.Add</c> does too — a file that never went
    /// through the parser cannot be bound.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>symbolTable: SymbolTable</c> is a JS <c>Map</c>, so its insertion order is observable; the
    /// port takes the ordered <see cref="SymbolTable"/> (§5 rule 17) and the two writes below are its indexer /
    /// <c>TryGetValue</c>.
    /// </para>
    /// </remarks>
    internal ISymbol DeclareSymbol(
        SymbolTable symbolTable,
        ISymbol? parent,
        IDeclaration declaration,
        SymbolFlags includes,
        SymbolFlags excludes,
        bool? isReplaceableByMethod = null,
        bool? isComputedName = null)
    {
        var node = (Node)declaration;
        Debug.Assert(Js.IsTruthy(isComputedName) || !Utilities.HasDynamicName(node));

        var isDefaultExport = Utilities.HasSyntacticModifier(node, ModifierFlags.Default)
            || (NodeTests.IsExportSpecifier(node) && Utilities.ModuleExportNameIsDefault(((ExportSpecifier)node).Name!));

        // The exported symbol for an export default function/class node is always named "default"
        var name = Js.IsTruthy(isComputedName) ? InternalSymbolName.Computed.Value
            : isDefaultExport && parent is not null ? InternalSymbolName.Default.Value
            : GetDeclarationName(declaration);

        ISymbol? symbol;
        if (name is null)
        {
            symbol = CreateSymbol(SymbolFlags.None, InternalSymbolName.Missing.Value);
        }
        else
        {
            // Check and see if the symbol table already has a symbol with this name.  If not,
            // create a new symbol with this name and add it to the table.  Note that we don't
            // give the new symbol any flags *yet*.  This ensures that it will not conflict
            // with the 'excludes' flags we pass in.
            //
            // If we do get an existing symbol, see if it conflicts with the new symbol we're
            // creating.  For example, a 'var' symbol and a 'class' symbol will conflict within
            // the same symbol table.  If we have a conflict, report the issue on each
            // declaration we have for this symbol, and then create a new symbol for this
            // declaration.
            //
            // Note that when properties declared in Javascript constructors
            // (marked by isReplaceableByMethod) conflict with another symbol, the property loses.
            // Always. This allows the common Javascript pattern of overwriting a prototype method
            // with an bound instance method of the same type: `this.method = this.method.bind(this)`
            //
            // If we created a new symbol, either because we didn't have a symbol with this name
            // in the symbol table, or we conflicted with an existing symbol, then just add this
            // node as the sole declaration of the new symbol.
            //
            // Otherwise, we'll be merging into a compatible existing symbol (for example when
            // you have multiple 'vars' with the same name in the same container).  In this case
            // just add this node into the declarations list of the symbol.
            symbol = symbolTable.TryGetValue(name, out var existing) ? existing : null;

            if ((includes & SymbolFlags.Classifiable) != 0)
            {
                _classifiableNames.Add(name);
            }

            if (symbol is null)
            {
                symbol = CreateSymbol(SymbolFlags.None, name);
                symbolTable[name] = symbol;
                if (Js.IsTruthy(isReplaceableByMethod)) symbol.IsReplaceableByMethod = true;
            }
            else if (Js.IsTruthy(isReplaceableByMethod) && symbol.IsReplaceableByMethod != true)
            {
                // A symbol already exists, so don't add this as a declaration.
                return symbol;
            }
            else if ((symbol.Flags & excludes) != 0)
            {
                if (symbol.IsReplaceableByMethod == true)
                {
                    // Javascript constructor-declared symbols can be discarded in favor of
                    // prototype symbols like methods.
                    symbol = CreateSymbol(SymbolFlags.None, name);
                    symbolTable[name] = symbol;
                }
                else if ((includes & SymbolFlags.Variable) == 0 || (symbol.Flags & SymbolFlags.Assignment) == 0)
                {
                    // Assignment declarations are allowed to merge with variables, no matter what other flags they have.
                    // TS's isNamedDeclaration guard plus node.name is the duck-typed slot read, which a JSX attribute
                    // also satisfies even though the model does not mark it INamedDeclaration.
                    if (Utilities.GetNamedDeclarationName(node) is { } declarationNameNode)
                    {
                        Utilities.SetParent(declarationNameNode, node);
                    }

                    // Report errors every position with duplicate declaration
                    // Report errors on previous encountered declarations
                    var message = (symbol.Flags & SymbolFlags.BlockScopedVariable) != 0
                        ? Diagnostics.Cannot_redeclare_block_scoped_variable_0
                        : Diagnostics.Duplicate_identifier_0;
                    var messageNeedsName = true;

                    if ((symbol.Flags & SymbolFlags.Enum) != 0 || (includes & SymbolFlags.Enum) != 0)
                    {
                        message = Diagnostics.Enum_declarations_can_only_merge_with_namespace_or_other_enum_declarations;
                        messageNeedsName = false;
                    }

                    var multipleDefaultExports = false;
                    if (Core.Length(symbol.Declarations) > 0)
                    {
                        // If the current node is a default export of some sort, then check if
                        // there are any other default exports that we need to error on.
                        // We'll know whether we have other default exports depending on if `symbol` already has a declaration list set.
                        if (isDefaultExport)
                        {
                            message = Diagnostics.A_module_cannot_have_multiple_default_exports;
                            messageNeedsName = false;
                            multipleDefaultExports = true;
                        }
                        else
                        {
                            // This is to properly report an error in the case "export default { }" is after export default of class declaration or function declaration.
                            // Error on multiple export default in the following case:
                            // 1. multiple export default of class declaration or function declaration by checking NodeFlags.Default
                            // 2. multiple export default of export assignment. This one doesn't have NodeFlags.Default on (as export default doesn't considered as modifiers)
                            if (node.Kind == SyntaxKind.ExportAssignment && ((ExportAssignment)node).IsExportEquals != true)
                            {
                                message = Diagnostics.A_module_cannot_have_multiple_default_exports;
                                messageNeedsName = false;
                                multipleDefaultExports = true;
                            }
                        }
                    }

                    var relatedInformation = new List<IDiagnosticRelatedInformation>();
                    if (NodeTests.IsTypeAliasDeclaration(node)
                        && Utilities.NodeIsMissing((Node?)((TypeAliasDeclaration)node).Type)
                        && Utilities.HasSyntacticModifier(node, ModifierFlags.Export)
                        && (symbol.Flags & (SymbolFlags.Alias | SymbolFlags.Type | SymbolFlags.Namespace)) != 0)
                    {
                        // export type T; - may have meant export type { T }?
                        relatedInformation.Add(CreateDiagnosticForNode(
                            node,
                            Diagnostics.Did_you_mean_0,
                            $"export type {{ {Utilities.UnescapeLeadingUnderscores(((TypeAliasDeclaration)node).Name!.EscapedText ?? "")} }}"));
                    }

                    var declarationName = (Node?)Utilities.GetNameOfDeclaration((Node)declaration) ?? node;
                    Core.ForEach(symbol.Declarations, (IDeclaration innerDeclaration, int index) =>
                    {
                        var decl = (Node?)Utilities.GetNameOfDeclaration((Node)innerDeclaration) ?? (Node)innerDeclaration;
                        var diag = messageNeedsName
                            ? CreateDiagnosticForNode(decl, message, GetDisplayName(innerDeclaration))
                            : CreateDiagnosticForNode(decl, message);
                        _file.BindDiagnostics!.Add(
                            multipleDefaultExports
                                ? Utilities.AddRelatedInfo(diag, CreateDiagnosticForNode(declarationName, index == 0 ? Diagnostics.Another_export_default_is_here : Diagnostics.and_here))
                                : diag);
                        if (multipleDefaultExports)
                        {
                            relatedInformation.Add(CreateDiagnosticForNode(decl, Diagnostics.The_first_export_default_is_here));
                        }

                        return (object?)null;
                    });

                    var diagLast = messageNeedsName
                        ? CreateDiagnosticForNode(declarationName, message, GetDisplayName(declaration))
                        : CreateDiagnosticForNode(declarationName, message);
                    _file.BindDiagnostics!.Add(Utilities.AddRelatedInfo(diagLast, [.. relatedInformation]));

                    symbol = CreateSymbol(SymbolFlags.None, name);
                }
            }
        }

        AddDeclarationToSymbol(symbol, declaration, includes);
        if (symbol.Parent is not null)
        {
            Debug.Assert(symbol.Parent == parent, "Existing symbol parent should match new one");
        }
        else
        {
            symbol.Parent = parent;
        }

        return symbol;
    }
}
