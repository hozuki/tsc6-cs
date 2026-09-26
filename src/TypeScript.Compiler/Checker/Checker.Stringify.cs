namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:5895-5979 — isValueSymbolAccessible, isSymbolAccessibleByFlags, isAnySymbolAccessible
// ported from: src/compiler/checker.ts:5980-6112 — isSymbolAccessible and the visibility walk
// ported from: src/compiler/checker.ts:6113-6168 — getMeaningOfEntityNameReference, isEntityNameVisible
// ported from: src/compiler/checker.ts:6169-6308 — the string converters and the flag/format helpers
// ported from: src/compiler/checker.ts:11168-11418 — the type-name formatters and isDeclarationVisible
/// <summary>
/// Symbol accessibility and the checker's string converters — the members of <c>createTypeChecker</c> that answer "can
/// this symbol be named from here", plus the four <c>…ToString</c> members and the display-name helpers they use.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the printer): <c>symbolToString</c> (6169), <c>signatureToString</c> (6202), <c>typeToString</c> (6240) and
/// <c>typePredicateToString</c> (11175) each end by handing a synthesized node to a printer
/// (<c>createPrinterWithRemoveCommentsNeverAsciiEscape</c> 6194, <c>createPrinterWithRemoveComments</c> 6195,
/// <c>createPrinterWithRemoveCommentsOmitTrailingSemicolon</c> 6205, <c>createPrinterWithDefaults</c> 6264) and calling
/// <c>printer.writeNode(...)</c>. The printer is declared in <c>emitter.ts:1200-1211</c> — Layer 7/M7, not ported. Per
/// docs/architecture.md §15.15 ruling 1 the port does not substitute a silently wrong string: each member builds the
/// node exactly as the reference does and then throws a loud, named blocker (<see cref="PrinterNotSupported"/>) whose
/// message carries the reference line. M7 replaces the throw with the printer call; every call site stays as it is.
/// These four are also listed in §15.16's residual table.
/// </para>
/// <para>
/// PORT-NOTE (<c>isAnySymbolAccessible</c> and its two neighbours, 5895-5979): the task brief's range for this slice
/// starts at 5980 (<c>isSymbolAccessible</c>), which is a one-line wrapper whose substance is
/// <c>isSymbolAccessibleWorker</c> (5984) and <c>isAnySymbolAccessible</c> (5905). The 85 lines before it are declared
/// nowhere in the port and belong to no other slice's band, so they are landed here with their own
/// <c>ported from:</c> lines; they are the accessibility family this slice's brief names.
/// </para>
/// <para>
/// PORT-NOTE (<c>isDeclarationVisible</c> 11324-11418): the brief's node-builder range ends at 11323, and this member is
/// the immediate neighbour that the <c>EmitResolver</c> requires (<c>IEmitResolver.IsDeclarationVisible</c>) and that
/// <c>hasVisibleDeclarations</c> calls, so it is landed here.
/// </para>
/// <para>
/// PORT-NOTE (three members it calls but does not declare): <c>getAccessibleSymbolChain</c> (5737-5840),
/// <c>getContainersOfSymbol</c> (5378-5462) and <c>getQualifiedLeftMeaning</c> (5732-5735), plus <c>needsQualification</c>
/// (5841-5862) which <c>getAccessibleSymbolChain</c> needs, fall past every slice band and are referenced unqualified
/// (they are <c>Checker</c> members). The same names are reached from <c>Checker/NodeBuilder.Names.cs</c>
/// (checker.ts:8479/8493), so one owner can close all of them.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Symbol accessibility (checker.ts:5895-6112)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:5895-5898 — <c>isValueSymbolAccessible</c>.</summary>
    internal bool IsValueSymbolAccessible(ISymbol typeSymbol, Node? enclosingDeclaration)
    {
        var access = IsSymbolAccessibleWorker(typeSymbol, enclosingDeclaration, SymbolFlags.Value, /*shouldComputeAliasesToMakeVisible*/ false, /*allowModules*/ true);
        return access.Accessibility == SymbolAccessibility.Accessible;
    }

    /// <summary>ported from: src/compiler/checker.ts:5900-5903 — <c>isSymbolAccessibleByFlags</c>.</summary>
    internal bool IsSymbolAccessibleByFlags(ISymbol typeSymbol, Node? enclosingDeclaration, SymbolFlags flags)
    {
        var access = IsSymbolAccessibleWorker(typeSymbol, enclosingDeclaration, flags, /*shouldComputeAliasesToMakeVisible*/ false, /*allowModules*/ false);
        return access.Accessibility == SymbolAccessibility.Accessible;
    }

    /// <summary>ported from: src/compiler/checker.ts:5905-5979 — <c>isAnySymbolAccessible</c>.</summary>
    internal ISymbolAccessibilityResult? IsAnySymbolAccessible(IList<ISymbol>? symbols, Node? enclosingDeclaration, ISymbol initialSymbol, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible, bool allowModules)
    {
        if (Core.Length(symbols) == 0) return null;

        ISymbol? hadAccessibleChain = null;
        var earlyModuleBail = false;
        foreach (var symbol in symbols!)
        {
            // Symbol is accessible if it by itself is accessible
            var accessibleSymbolChain = GetAccessibleSymbolChain(symbol, enclosingDeclaration, meaning, /*useOnlyExternalAliasing*/ false);
            if (accessibleSymbolChain is not null)
            {
                hadAccessibleChain = symbol;
                var hasAccessibleDeclarations = HasVisibleDeclarations(accessibleSymbolChain[0], shouldComputeAliasesToMakeVisible);
                if (hasAccessibleDeclarations is not null)
                {
                    return hasAccessibleDeclarations;
                }
            }
            if (allowModules)
            {
                if (Core.Some(symbol.Declarations, d => HasNonGlobalAugmentationExternalModuleSymbol((Node)d)))
                {
                    if (shouldComputeAliasesToMakeVisible)
                    {
                        earlyModuleBail = true;
                        // Generally speaking, we want to use the aliases that already exist to refer to a module, if present
                        // In order to do so, we need to find those aliases in order to retain them in declaration emit; so
                        // if we are in declaration emit, we cannot use the fast path for module visibility until we've exhausted
                        // all other visibility options (in order to capture the possible aliases used to reference the module)
                        continue;
                    }
                    // Any meaning of a module symbol is always accessible via an `import` type
                    return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible };
                }
            }

            // If we haven't got the accessible symbol, it doesn't mean the symbol is actually inaccessible.
            // It could be a qualified symbol and hence verify the path
            // e.g.:
            // module m {
            //     export class c {
            //     }
            // }
            // const x: typeof m.c
            // In the above example when we start with checking if typeof m.c symbol is accessible,
            // we are going to see if c can be accessed in scope directly.
            // But it can't, hence the accessible is going to be undefined, but that doesn't mean m.c is inaccessible
            // It is accessible if the parent m is accessible because then m.c can be accessed through qualification

            var containers = GetContainersOfSymbol(symbol, enclosingDeclaration, meaning);
            var parentResult = IsAnySymbolAccessible(containers, enclosingDeclaration, initialSymbol, ReferenceEquals(initialSymbol, symbol) ? GetQualifiedLeftMeaning(meaning) : meaning, shouldComputeAliasesToMakeVisible, allowModules);
            if (parentResult is not null)
            {
                return parentResult;
            }
        }

        if (earlyModuleBail)
        {
            return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible };
        }

        if (hadAccessibleChain is not null)
        {
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotAccessible,
                ErrorSymbolName = SymbolToString(initialSymbol, enclosingDeclaration, meaning),
                ErrorModuleName = !ReferenceEquals(hadAccessibleChain, initialSymbol) ? SymbolToString(hadAccessibleChain, enclosingDeclaration, SymbolFlags.Namespace) : null,
            };
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5980-5983 — <c>isSymbolAccessible</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>IEmitResolver.IsSymbolAccessible</c> spells <c>meaning</c> as <c>SymbolFlags?</c> while
    /// <c>ITypeChecker.IsSymbolAccessible</c> spells it as <c>SymbolFlags</c> (NodeInterfaces.cs:826 / :3660). C# matches
    /// interface members on the exact parameter type, so the two slots need two overloads; the nullable one is the
    /// implementation and the non-nullable one forwards. A call with a non-nullable <c>meaning</c> picks the exact match.
    /// </remarks>
    internal ISymbolAccessibilityResult IsSymbolAccessible(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, bool shouldComputeAliasesToMarkVisible)
        => IsSymbolAccessibleWorker(symbol, enclosingDeclaration, meaning ?? SymbolFlags.None, shouldComputeAliasesToMarkVisible, /*allowModules*/ true);

    /// <summary>ported from: src/compiler/checker.ts:5980-5983 — the <c>ITypeChecker.IsSymbolAccessible</c> face.</summary>
    public ISymbolAccessibilityResult IsSymbolAccessible(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasToMarkVisible)
        => IsSymbolAccessibleWorker(symbol, enclosingDeclaration, meaning, shouldComputeAliasToMarkVisible, /*allowModules*/ true);

    /// <summary>ported from: src/compiler/checker.ts:5984-6016 — <c>isSymbolAccessibleWorker</c>.</summary>
    internal ISymbolAccessibilityResult IsSymbolAccessibleWorker(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible, bool allowModules)
    {
        if (symbol is not null && enclosingDeclaration is not null)
        {
            var result = IsAnySymbolAccessible([symbol], enclosingDeclaration, symbol, meaning, shouldComputeAliasesToMakeVisible, allowModules);
            if (result is not null)
            {
                return result;
            }

            // This could be a symbol that is not exported in the external module
            // or it could be a symbol from different external module that is not aliased and hence cannot be named
            ISymbol? symbolExternalModule = null;
            if (symbol.Declarations is not null)
            {
                foreach (var declaration in symbol.Declarations)
                {
                    symbolExternalModule = GetExternalModuleContainer((Node)declaration);
                    if (symbolExternalModule is not null) break;
                }
            }
            if (symbolExternalModule is not null)
            {
                var enclosingExternalModule = GetExternalModuleContainer(enclosingDeclaration);
                if (!ReferenceEquals(symbolExternalModule, enclosingExternalModule))
                {
                    // name from different external module that is not visible
                    return new SymbolAccessibilityResult
                    {
                        Accessibility = SymbolAccessibility.CannotBeNamed,
                        ErrorSymbolName = SymbolToString(symbol, enclosingDeclaration, meaning),
                        ErrorModuleName = SymbolToString(symbolExternalModule),
                        ErrorNode = Utilities.IsInJSFile(enclosingDeclaration) ? enclosingDeclaration : null,
                    };
                }
            }

            // Just a local name that is not accessible
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotAccessible,
                ErrorSymbolName = SymbolToString(symbol, enclosingDeclaration, meaning),
            };
        }

        return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible };
    }

    /// <summary>ported from: src/compiler/checker.ts:6017-6021 — <c>getExternalModuleContainer</c>.</summary>
    internal ISymbol? GetExternalModuleContainer(Node declaration)
    {
        var node = Utilities.FindAncestor(declaration, HasExternalModuleSymbol);
        return node is not null ? GetSymbolOfDeclaration((IDeclaration)node) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:6022-6025 — <c>hasExternalModuleSymbol</c>.</summary>
    private static bool HasExternalModuleSymbol(Node declaration)
        => Utilities.IsAmbientModule(declaration)
            || (declaration.Kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule((SourceFile)declaration));

    /// <summary>ported from: src/compiler/checker.ts:6026-6029 — <c>hasNonGlobalAugmentationExternalModuleSymbol</c>.</summary>
    private static bool HasNonGlobalAugmentationExternalModuleSymbol(Node declaration)
        => Utilities.IsModuleWithStringLiteralName(declaration)
            || (declaration.Kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule((SourceFile)declaration));

    /// <summary>ported from: src/compiler/checker.ts:6030-6112 — <c>hasVisibleDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>SymbolVisibilityResult | undefined</c>, but every caller returns the value as a
    /// <c>SymbolAccessibilityResult</c> and the object built here satisfies it (no <c>errorModuleName</c> is TS's
    /// <c>undefined</c>). The port states the derived interface, which is what the generated model can express;
    /// <c>ISymbolAccessibilityResult : ISymbolVisibilityResult</c> (NodeInterfaces.cs:3180), so the base-typed slots
    /// still accept it.
    /// </remarks>
    internal ISymbolAccessibilityResult? HasVisibleDeclarations(ISymbol symbol, bool shouldComputeAliasToMakeVisible)
    {
        List<Node>? aliasesToMakeVisible = null;
        if (!Core.Every(Core.Filter(symbol.Declarations, d => ((Node)d).Kind != SyntaxKind.Identifier), (d, _) => GetIsDeclarationVisible((Node)d)))
        {
            return null;
        }
        return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible, AliasesToMakeVisible = aliasesToMakeVisible };

        bool GetIsDeclarationVisible(Node declaration)
        {
            if (!IsDeclarationVisible(declaration))
            {
                // Mark the unexported alias as visible if its parent is visible
                // because these kind of aliases can be used to name types in declaration file

                var anyImportSyntax = GetAnyImportSyntax(declaration);
                if (
                    anyImportSyntax is not null &&
                    !Utilities.HasSyntacticModifier(anyImportSyntax, ModifierFlags.Export) && // import clause without export
                    IsDeclarationVisible(anyImportSyntax.Parent)
                )
                {
                    return AddVisibleAlias(declaration, anyImportSyntax);
                }
                else if (
                    NodeTests.IsVariableDeclaration(declaration) && NodeTests.IsVariableStatement(declaration.Parent!.Parent!) &&
                    !Utilities.HasSyntacticModifier(declaration.Parent.Parent!, ModifierFlags.Export) && // unexported variable statement
                    IsDeclarationVisible(declaration.Parent!.Parent!.Parent!)
                )
                {
                    return AddVisibleAlias(declaration, declaration.Parent.Parent);
                }
                else if (
                    Utilities.IsLateVisibilityPaintedStatement(declaration) // unexported top-level statement
                    && !Utilities.HasSyntacticModifier(declaration, ModifierFlags.Export)
                    && IsDeclarationVisible(declaration.Parent)
                )
                {
                    return AddVisibleAlias(declaration, declaration);
                }
                else if (NodeTests.IsBindingElement(declaration))
                {
                    if (
                        (symbol.Flags & SymbolFlags.Alias) != 0 && Utilities.IsInJSFile(declaration) && declaration.Parent?.Parent is not null // exported import-like top-level JS require statement
                        && NodeTests.IsVariableDeclaration(declaration.Parent.Parent)
                        && declaration.Parent.Parent.Parent?.Parent is not null && NodeTests.IsVariableStatement(declaration.Parent.Parent.Parent.Parent)
                        && !Utilities.HasSyntacticModifier(declaration.Parent.Parent.Parent.Parent, ModifierFlags.Export)
                        && declaration.Parent.Parent.Parent.Parent.Parent is not null // check if the thing containing the variable statement is visible (ie, the file)
                        && IsDeclarationVisible(declaration.Parent.Parent.Parent.Parent.Parent)
                    )
                    {
                        return AddVisibleAlias(declaration, declaration.Parent.Parent.Parent.Parent);
                    }
                    else if ((symbol.Flags & SymbolFlags.BlockScopedVariable) != 0)
                    {
                        var rootDeclaration = Utilities.WalkUpBindingElementsAndPatterns((BindingElement)declaration);
                        if (rootDeclaration.Kind == SyntaxKind.Parameter)
                        {
                            return false;
                        }
                        var variableStatement = rootDeclaration.Parent!.Parent!;
                        if (variableStatement.Kind != SyntaxKind.VariableStatement)
                        {
                            return false;
                        }
                        if (Utilities.HasSyntacticModifier(variableStatement, ModifierFlags.Export))
                        {
                            return true;
                        }
                        if (!IsDeclarationVisible(variableStatement.Parent!))
                        {
                            return false;
                        }
                        return AddVisibleAlias(declaration, variableStatement);
                    }
                }

                // Declaration is not visible
                return false;
            }

            return true;
        }

        bool AddVisibleAlias(Node declaration, Node aliasingStatement)
        {
            // In function "buildTypeDisplay" where we decide whether to write type-alias or serialize types,
            // we want to just check if type- alias is accessible or not but we don't care about emitting those alias at that time
            // since we will do the emitting later in trackSymbol.
            if (shouldComputeAliasToMakeVisible)
            {
                GetNodeLinks(declaration).IsVisible = true;
                // PORT-NOTE: TS's `appendIfUnique(aliases, x)` shares one implementation with `Core.AppendIfUnique`,
                // whose result is the same list instance; the port keeps the list's declared type (`List<Node>`, the
                // generated shape of TS's `LateVisibilityPaintedStatement[]`) and pushes in place, which is the same
                // reference-equality test for nodes.
                aliasesToMakeVisible ??= [];
                Core.PushIfUnique(aliasesToMakeVisible, aliasingStatement);
            }
            return true;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Entity-name visibility (checker.ts:6113-6168)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:6113-6142 — <c>getMeaningOfEntityNameReference</c>.</summary>
    private SymbolFlags GetMeaningOfEntityNameReference(Node entityName)
    {
        // get symbol of the first identifier of the entityName
        SymbolFlags meaning;
        var parentKind = entityName.Parent!.Kind;
        if (
            parentKind == SyntaxKind.TypeQuery ||
            (parentKind == SyntaxKind.ExpressionWithTypeArguments && !Utilities.IsPartOfTypeNode(entityName.Parent)) ||
            parentKind == SyntaxKind.ComputedPropertyName ||
            (parentKind == SyntaxKind.TypePredicate && ReferenceEquals(((TypePredicateNode)entityName.Parent).ParameterName, entityName))
        )
        {
            // Typeof value
            meaning = SymbolFlags.Value | SymbolFlags.ExportValue;
        }
        else if (
            entityName.Kind == SyntaxKind.QualifiedName || entityName.Kind == SyntaxKind.PropertyAccessExpression ||
            parentKind == SyntaxKind.ImportEqualsDeclaration ||
            (parentKind == SyntaxKind.QualifiedName && ReferenceEquals(((QualifiedName)entityName.Parent).Left, entityName)) ||
            (parentKind == SyntaxKind.PropertyAccessExpression && ReferenceEquals(((PropertyAccessExpression)entityName.Parent).Expression, entityName)) ||
            (parentKind == SyntaxKind.ElementAccessExpression && ReferenceEquals(((ElementAccessExpression)entityName.Parent).Expression, entityName))
        )
        {
            // Left identifier from type reference or TypeAlias
            // Entity name of the import declaration
            meaning = SymbolFlags.Namespace;
        }
        else
        {
            // Type Reference or TypeAlias entity = Identifier
            meaning = SymbolFlags.Type;
        }
        return meaning;
    }

    /// <summary>ported from: src/compiler/checker.ts:6143-6168 — <c>isEntityNameVisible</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's third parameter defaults to <c>true</c>; the port keeps the default, which also makes this one
    /// member implement both two-parameter slots (<c>ITypeChecker.IsEntityNameVisible</c> and
    /// <c>IEmitResolver.IsEntityNameVisible</c>).
    /// </remarks>
    public ISymbolVisibilityResult IsEntityNameVisible(Node? entityName, Node? enclosingDeclaration, bool shouldComputeAliasToMakeVisible = true)
    {
        var meaning = GetMeaningOfEntityNameReference(entityName!);
        var firstIdentifier = Utilities.GetFirstIdentifier(entityName!)!;
        var symbol = ResolveName(enclosingDeclaration, firstIdentifier.EscapedText!, meaning, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
        if (symbol is not null && (symbol.Flags & SymbolFlags.TypeParameter) != 0 && (meaning & SymbolFlags.Type) != 0)
        {
            return new SymbolVisibilityResult { Accessibility = SymbolAccessibility.Accessible };
        }
        if (symbol is null && Utilities.IsThisIdentifier(firstIdentifier) && IsSymbolAccessible(GetSymbolOfDeclaration((IDeclaration)Utilities.GetThisContainer(firstIdentifier, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false)), firstIdentifier, meaning, /*shouldComputeAliasesToMakeVisible*/ false).Accessibility == SymbolAccessibility.Accessible)
        {
            return new SymbolVisibilityResult { Accessibility = SymbolAccessibility.Accessible };
        }

        if (symbol is null)
        {
            return new SymbolVisibilityResult
            {
                Accessibility = SymbolAccessibility.NotResolved,
                ErrorSymbolName = Utilities.GetTextOfNode(firstIdentifier),
                ErrorNode = firstIdentifier,
            };
        }
        // Verify if the symbol is accessible
        return HasVisibleDeclarations(symbol, shouldComputeAliasToMakeVisible) ?? new SymbolAccessibilityResult
        {
            Accessibility = SymbolAccessibility.NotAccessible,
            ErrorSymbolName = Utilities.GetTextOfNode(firstIdentifier),
            ErrorNode = firstIdentifier,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // The string converters (checker.ts:6169-6308) — printer-gated, see the class remarks
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:6169-6201 — <c>symbolToString</c>.</summary>
    public string SymbolToString(ISymbol? symbol, Node? enclosingDeclaration = null, SymbolFlags? meaning = null, SymbolFormatFlags? flags = null)
        => SymbolToString(symbol, enclosingDeclaration, meaning, flags ?? SymbolFormatFlags.AllowAnyNodeKind, /*writer*/ null);

    /// <summary>ported from: src/compiler/checker.ts:6169-6201 — the writer-taking overload TS expresses as <c>writer?</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbolToStringWorker</c> is a nested function that closes over this member's parameters, so
    /// the port states it as a local function (the landed convention, <c>Checker.Conditional.cs:328</c>).
    /// </remarks>
    internal string SymbolToString(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, SymbolFormatFlags flags, IEmitTextWriter? writer)
    {
        return writer is not null ? SymbolToStringWorker(writer).GetText() : Utilities.UsingSingleLineStringWriter(SymbolToStringWorker);

        IEmitTextWriter SymbolToStringWorker(IEmitTextWriter writer)
        {
        var nodeFlags = NodeBuilderFlags.IgnoreErrors;
        var internalNodeFlags = InternalNodeBuilderFlags.None;
        if ((flags & SymbolFormatFlags.UseOnlyExternalAliasing) != 0)
        {
            nodeFlags |= NodeBuilderFlags.UseOnlyExternalAliasing;
        }
        if ((flags & SymbolFormatFlags.WriteTypeParametersOrArguments) != 0)
        {
            nodeFlags |= NodeBuilderFlags.WriteTypeParametersInQualifiedName;
        }
        if ((flags & SymbolFormatFlags.UseAliasDefinedOutsideCurrentScope) != 0)
        {
            nodeFlags |= NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope;
        }
        if ((flags & SymbolFormatFlags.DoNotIncludeSymbolChain) != 0)
        {
            internalNodeFlags |= InternalNodeBuilderFlags.DoNotIncludeSymbolChain;
        }
        if ((flags & SymbolFormatFlags.WriteComputedProps) != 0)
        {
            internalNodeFlags |= InternalNodeBuilderFlags.WriteComputedProps;
        }
        // PORT-NOTE: TS picks the builder with a ternary on one `flags` bit and calls it with five arguments; both
        // members have the identical six-parameter port signature, so the choice is a delegate assignment here.
        // PORT-NOTE: TS passes `meaning!` (a value that may be `undefined` at run time); the port normalises the absent
        // meaning to `SymbolFlags.None`, which is what every bitwise test against `undefined` answers in JS.
        Func<ISymbol?, SymbolFlags, Node?, NodeBuilderFlags?, InternalNodeBuilderFlags?, ISymbolTracker?, Node?> builder =
            (flags & SymbolFormatFlags.AllowAnyNodeKind) != 0 ? SymbolToNode : SymbolToEntityName;
        _ = builder(symbol, meaning ?? SymbolFlags.None, enclosingDeclaration, nodeFlags, internalNodeFlags, /*tracker*/ null)!; // TODO: GH#18217
        // add neverAsciiEscape for GH#39027
        // PORT-NOTE: TS selects the printer here (`enclosingDeclaration?.kind === SyntaxKind.SourceFile`
        // ? createPrinterWithRemoveCommentsNeverAsciiEscape() : createPrinterWithRemoveComments()) and prints `entity`;
        // both printers are M7's. See the class remarks.
        throw PrinterNotSupported(nameof(SymbolToString), 6200);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:6202-6239 — <c>signatureToString</c>.</summary>
    public string SignatureToString(ISignature? signature, Node? enclosingDeclaration = null, TypeFormatFlags? flags = null, SignatureKind? kind = null)
        => SignatureToString(signature, enclosingDeclaration, flags ?? TypeFormatFlags.None, kind, /*writer*/ null, /*maximumLength*/ null, /*verbosityLevel*/ null, /*out*/ null);

    /// <summary>ported from: src/compiler/checker.ts:6202-6239 — the writer-taking overload.</summary>
    internal string SignatureToString(ISignature? signature, Node? enclosingDeclaration, TypeFormatFlags flags, SignatureKind? kind, IEmitTextWriter? writer, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
    {
        return writer is not null ? SignatureToStringWorker(writer).GetText() : Utilities.UsingSingleLineStringWriter(SignatureToStringWorker);

        IEmitTextWriter SignatureToStringWorker(IEmitTextWriter writer)
        {
        SyntaxKind sigOutput;
        if ((flags & TypeFormatFlags.WriteArrowStyleSignature) != 0)
        {
            sigOutput = kind == SignatureKind.Construct ? SyntaxKind.ConstructorType : SyntaxKind.FunctionType;
        }
        else
        {
            sigOutput = kind == SignatureKind.Construct ? SyntaxKind.ConstructSignature : SyntaxKind.CallSignature;
        }
        _ = SignatureToSignatureDeclaration(
            signature!,
            sigOutput,
            enclosingDeclaration,
            ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.WriteTypeParametersInQualifiedName,
            /*internalFlags*/ null,
            /*tracker*/ null,
            maximumLength,
            verbosityLevel,
            @out);
        // PORT-NOTE: TS prints `sig` with `createPrinterWithRemoveCommentsOmitTrailingSemicolon()` into
        // `getTrailingSemicolonDeferringWriter(writer)` (checker.ts:6233-6236); the printer is M7's and the deferring
        // writer is utilities.ts:6412 (M6 utilities slice). See the class remarks.
        throw PrinterNotSupported(nameof(SignatureToString), 6233);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:6240-6265 — <c>typeToString</c>.</summary>
    public string TypeToString(IType? type, Node? enclosingDeclaration = null, TypeFormatFlags? flags = null)
        => TypeToString(type, enclosingDeclaration, flags ?? (TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.UseAliasDefinedOutsideCurrentScope), /*writer*/ null, /*maximumLength*/ null, /*verbosityLevel*/ null, /*out*/ null);

    /// <summary>ported from: src/compiler/checker.ts:6240-6265 — the writer-taking overload.</summary>
    internal string TypeToString(IType? type, Node? enclosingDeclaration, TypeFormatFlags flags, IEmitTextWriter? writer, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
    {
        return writer is not null ? TypeToStringWorker(writer).GetText() : Utilities.UsingSingleLineStringWriter(TypeToStringWorker);

        IEmitTextWriter TypeToStringWorker(IEmitTextWriter writer)
        {
        // PORT-NOTE: TS's `!maximumLength` is JS falsiness, so an explicit 0 counts as "no maximum" too.
        var noTruncation = ((maximumLength ?? 0) == 0 && _compilerOptions.NoErrorTruncation == true)
            || (flags & TypeFormatFlags.NoTruncation) != 0;
        var typeNode = TypeToTypeNode(
            type!,
            enclosingDeclaration,
            ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | (noTruncation ? NodeBuilderFlags.NoTruncation : NodeBuilderFlags.None),
            /*internalFlags*/ null,
            /*tracker*/ null,
            maximumLength,
            verbosityLevel,
            @out);
        if (typeNode is null) Debug.Fail("should always get typenode");
        // The unresolved type gets a synthesized comment on `any` to hint to users that it's not a plain `any`.
        // Otherwise, we always strip comments out.
        // PORT-NOTE: TS picks between `createPrinterWithRemoveComments()` and `createPrinterWithDefaults()` here
        // (checker.ts:6262) according to whether the type is `unresolvedType`, prints `typeNode`, then trims the result
        // to the maximum length (6266-6272). Both printers are M7's, so the print and the trim move with it.
        throw PrinterNotSupported(nameof(TypeToString), 6262);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:11175-11187 — <c>typePredicateToString</c>.</summary>
    public string TypePredicateToString(ITypePredicateBase? predicate, Node? enclosingDeclaration = null, TypeFormatFlags? flags = null)
        => TypePredicateToString(predicate, enclosingDeclaration, flags ?? TypeFormatFlags.UseAliasDefinedOutsideCurrentScope, /*writer*/ null);

    /// <summary>ported from: src/compiler/checker.ts:11175-11187 — the writer-taking overload.</summary>
    internal string TypePredicateToString(ITypePredicateBase? predicate, Node? enclosingDeclaration, TypeFormatFlags flags, IEmitTextWriter? writer)
    {
        return writer is not null ? TypePredicateToStringWorker(writer).GetText() : Utilities.UsingSingleLineStringWriter(TypePredicateToStringWorker);

        IEmitTextWriter TypePredicateToStringWorker(IEmitTextWriter writer)
        {
        var nodeBuilderFlags = ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.WriteTypeParametersInQualifiedName;
        _ = TypePredicateToTypePredicateNode(predicate, enclosingDeclaration, nodeBuilderFlags, /*internalFlags*/ null, /*tracker*/ null)!; // TODO: GH#18217
        // PORT-NOTE: TS prints the predicate with `createPrinterWithRemoveComments()` (checker.ts:11181); that printer is
        // M7's. See the class remarks.
        throw PrinterNotSupported(nameof(TypePredicateToString), 11181);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:1738-1748 — <c>writeSignature</c>.</summary>
    public string WriteSignature(ISignature? signature, Node? enclosingDeclaration, TypeFormatFlags? flags, SignatureKind? kind, IEmitTextWriter? writer, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => SignatureToString(signature, enclosingDeclaration, flags ?? TypeFormatFlags.None, kind, writer, maximumLength, verbosityLevel, @out);

    /// <summary>ported from: src/compiler/checker.ts:1741-1743 — <c>writeType</c>.</summary>
    public string WriteType(IType? type, Node? enclosingDeclaration, TypeFormatFlags? flags, IEmitTextWriter? writer, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => TypeToString(type, enclosingDeclaration, flags ?? (TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.UseAliasDefinedOutsideCurrentScope), writer, maximumLength, verbosityLevel, @out);

    /// <summary>ported from: src/compiler/checker.ts:1744-1746 — <c>writeSymbol</c>.</summary>
    public string WriteSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, SymbolFormatFlags? flags, IEmitTextWriter? writer)
        => SymbolToString(symbol, enclosingDeclaration, meaning, flags ?? SymbolFormatFlags.AllowAnyNodeKind, writer);

    /// <summary>ported from: src/compiler/checker.ts:1747-1749 — <c>writeTypePredicate</c>.</summary>
    public string WriteTypePredicate(ITypePredicateBase? predicate, Node? enclosingDeclaration, TypeFormatFlags? flags, IEmitTextWriter? writer)
        => TypePredicateToString(predicate, enclosingDeclaration, flags ?? TypeFormatFlags.UseAliasDefinedOutsideCurrentScope, writer);

    // ---------------------------------------------------------------------------------------------
    // The display-name helpers (checker.ts:6266-6308, 11168-11323)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:6266-6275 — <c>getTypeNamesForErrorDisplay</c>.</summary>
    internal (string Left, string Right) GetTypeNamesForErrorDisplay(IType left, IType right)
    {
        var leftStr = SymbolValueDeclarationIsContextSensitive(left.Symbol!) ? TypeToString(left, (Node?)left.Symbol!.ValueDeclaration) : TypeToString(left);
        var rightStr = SymbolValueDeclarationIsContextSensitive(right.Symbol!) ? TypeToString(right, (Node?)right.Symbol!.ValueDeclaration) : TypeToString(right);
        if (leftStr == rightStr)
        {
            leftStr = GetTypeNameForErrorDisplay(left);
            rightStr = GetTypeNameForErrorDisplay(right);
        }
        return (leftStr, rightStr);
    }

    /// <summary>ported from: src/compiler/checker.ts:6276-6278 — <c>getTypeNameForErrorDisplay</c>.</summary>
    internal string GetTypeNameForErrorDisplay(IType type)
        => TypeToString(type, /*enclosingDeclaration*/ null, TypeFormatFlags.UseFullyQualifiedType);

    /// <summary>ported from: src/compiler/checker.ts:6280-6282 — <c>symbolValueDeclarationIsContextSensitive</c>.</summary>
    internal bool SymbolValueDeclarationIsContextSensitive(ISymbol symbol)
        => symbol is not null && symbol.ValueDeclaration is not null && Utilities.IsExpression((Node)symbol.ValueDeclaration) && !IsContextSensitive((Node)symbol.ValueDeclaration);

    /// <summary>ported from: src/compiler/checker.ts:6284-6286 — <c>toNodeBuilderFlags</c>.</summary>
    internal NodeBuilderFlags ToNodeBuilderFlags(TypeFormatFlags flags = TypeFormatFlags.None)
        => (NodeBuilderFlags)(flags & TypeFormatFlags.NodeBuilderFlagsMask);

    /// <summary>ported from: src/compiler/checker.ts:6288-6290 — <c>isClassInstanceSide</c>.</summary>
    internal bool IsClassInstanceSide(IType type)
        => type.Symbol is not null && (type.Symbol.Flags & SymbolFlags.Class) != 0
            && (ReferenceEquals(type, GetDeclaredTypeOfClassOrInterface(type.Symbol))
                || ((type.Flags & TypeFlags.Object) != 0 && (GetObjectFlags(type) & ObjectFlags.IsClassInstanceClone) != 0));

    /// <summary>ported from: src/compiler/checker.ts:6292-6299 — <c>getTypeFromTypeNodeWithoutContext</c>.</summary>
    /// <remarks>
    /// Same as <c>getTypeFromTypeNode</c>, but for use in <c>createNodeBuilder</c>. Inside <c>createNodeBuilder</c> TS
    /// shadows <c>getTypeFromTypeNode</c> to make sure anyone using this function will call the local version that does
    /// type mapping if appropriate; this function is used to still be able to call the original <c>getTypeFromTypeNode</c>
    /// from the local scope version of it.
    /// </remarks>
    internal IType GetTypeFromTypeNodeWithoutContext(ITypeNode node)
        => GetTypeFromTypeNode(node);

    /// <summary>ported from: src/compiler/checker.ts:11170-11173 — <c>isLibType</c>.</summary>
    /// <remarks>Returns true if a type is declared in a lib file. Don't expand types like <c>Array</c> or <c>Promise</c>, instead treating them as opaque.</remarks>
    public bool IsLibType(IType? type)
    {
        var symbol = (GetObjectFlags(type!) & ObjectFlags.Reference) != 0 ? ((ITypeReference)type!).Target!.Symbol : type!.Symbol;
        return IsTupleType(type) || Core.Some(symbol?.Declarations, decl => _host.IsSourceFileDefaultLibrary(Utilities.GetSourceFileOfNode((Node)decl)));
    }

    /// <summary>ported from: src/compiler/checker.ts:11188-11213 — <c>formatUnionTypes</c>.</summary>
    internal List<IType> FormatUnionTypes(IList<IType> types, bool expandingEnum)
    {
        var result = new List<IType>();
        var flags = (TypeFlags)0;
        for (var i = 0; i < types.Count; i++)
        {
            var t = types[i];
            flags |= t.Flags;
            if ((t.Flags & TypeFlags.Nullable) == 0)
            {
                if ((t.Flags & TypeFlags.BooleanLiteral) != 0 || (!expandingEnum && (t.Flags | TypeFlags.EnumLike) != 0))
                {
                    // Reference (checker.ts:11196): `getBaseTypeOfEnumLikeType(t as LiteralType)` — the reference's cast
                    // is erased, and `getBaseTypeOfEnumLikeType` itself takes a `Type` and tests `TypeFlags.EnumLike`
                    // before reading anything literal-only, so every type that satisfies the enclosing guard may be
                    // passed: an `IntrinsicType`/`InstantiationExpressionType` is answered unchanged, while the old
                    // concrete `(LiteralType)` cast threw `InvalidCastException` for them.
                    var baseType = (t.Flags & TypeFlags.BooleanLiteral) != 0 ? _booleanType : GetBaseTypeOfEnumLikeType(t);
                    if ((baseType.Flags & TypeFlags.Union) != 0)
                    {
                        var count = ((IUnionType)baseType).Types!.Count;
                        if (i + count <= types.Count && ReferenceEquals(GetRegularTypeOfLiteralType(types[i + count - 1]), GetRegularTypeOfLiteralType(((IUnionType)baseType).Types![count - 1])))
                        {
                            result.Add(baseType);
                            i += count - 1;
                            continue;
                        }
                    }
                }
                result.Add(t);
            }
        }
        if ((flags & TypeFlags.Null) != 0) result.Add(_nullType);
        if ((flags & TypeFlags.Undefined) != 0) result.Add(_undefinedType);
        return result.Count > 0 ? result : [.. types];
    }

    /// <summary>ported from: src/compiler/checker.ts:11214-11222 — <c>visibilityToString</c>.</summary>
    internal static string VisibilityToString(ModifierFlags flags)
    {
        if (flags == ModifierFlags.Private)
        {
            return "private";
        }
        if (flags == ModifierFlags.Protected)
        {
            return "protected";
        }
        return "public";
    }

    /// <summary>ported from: src/compiler/checker.ts:11224-11232 — <c>getTypeAliasForTypeLiteral</c>.</summary>
    internal ISymbol? GetTypeAliasForTypeLiteral(IType type)
    {
        if (type.Symbol is not null && (type.Symbol.Flags & SymbolFlags.TypeLiteral) != 0 && type.Symbol.Declarations is not null)
        {
            var node = Utilities.WalkUpParenthesizedTypes(((Node)type.Symbol.Declarations[0]).Parent!);
            if (NodeTests.IsTypeAliasDeclaration(node))
            {
                return GetSymbolOfDeclaration((IDeclaration)node);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11234-11238 — <c>isTopLevelInExternalModuleAugmentation</c>.</summary>
    internal static bool IsTopLevelInExternalModuleAugmentation(Node? node)
        => node is not null && node.Parent is not null
            && node.Parent.Kind == SyntaxKind.ModuleBlock
            && Utilities.IsExternalModuleAugmentation(node.Parent.Parent!);

    /// <summary>ported from: src/compiler/checker.ts:11240-11242 — <c>isDefaultBindingContext</c>.</summary>
    internal static bool IsDefaultBindingContext(Node location)
        => location.Kind == SyntaxKind.SourceFile || Utilities.IsAmbientModule(location);

    /// <summary>ported from: src/compiler/checker.ts:11244-11268 — <c>getNameOfSymbolFromNameType</c>.</summary>
    internal string? GetNameOfSymbolFromNameType(ISymbol symbol, NodeBuilderContext? context = null)
    {
        var nameType = GetSymbolLinks(symbol).NameType;
        if (nameType is not null)
        {
            if ((nameType.Flags & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var name = "" + ((ILiteralType)nameType).Value;
                if (!ScannerUtilities.IsIdentifierText(name, Utilities.GetEmitScriptTarget(_compilerOptions)) && !Utilities.IsNumericLiteralName(name))
                {
                    return "\"" + Utilities.EscapeString(name, CharacterCodes.DoubleQuote) + "\"";
                }
                if (Utilities.IsNumericLiteralName(name) && Core.StartsWith(name, "-"))
                {
                    return "[" + name + "]";
                }
                return name;
            }
            if ((nameType.Flags & TypeFlags.UniqueESSymbol) != 0)
            {
                return "[" + GetNameOfSymbolAsWritten(((IUniqueESSymbolType)nameType).Symbol!, context) + "]";
            }
        }
        return null;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:11275-11323 — <c>getNameOfSymbolAsWritten</c>.
    /// </summary>
    /// <remarks>
    /// Gets a human-readable name for a symbol. Should *not* be used for the right-hand side of a `.` — use
    /// <c>Utilities.SymbolName</c> for that instead. Unlike <c>SymbolName</c>, this will include quotes if the name is
    /// from a string literal. It will also use a representation of a number as written instead of a decimal form, e.g.
    /// <c>0o11</c> instead of <c>9</c>.
    /// </remarks>
    internal string GetNameOfSymbolAsWritten(ISymbol symbol, NodeBuilderContext? context = null)
    {
        if (context?.RemappedSymbolReferences is not null && context.RemappedSymbolReferences.ContainsKey(Utilities.GetSymbolId(symbol)))
        {
            symbol = context.RemappedSymbolReferences[Utilities.GetSymbolId(symbol)];
        }
        if (
            context is not null && symbol.EscapedName == InternalSymbolName.Default.Value && (context.Flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) == 0 &&
            // If it's not the first part of an entity name, it must print as `default`
            ((context.Flags & NodeBuilderFlags.InInitialEntityName) == 0 ||
                // if the symbol is synthesized, it will only be referenced externally it must print as `default`
                symbol.Declarations is null ||
                // if not in the same binding context (source file, module declaration), it must print as `default`
                (context.EnclosingDeclaration is not null && !ReferenceEquals(Utilities.FindAncestor((Node)symbol.Declarations[0], IsDefaultBindingContext), Utilities.FindAncestor(context.EnclosingDeclaration, IsDefaultBindingContext))))
        )
        {
            return "default";
        }
        if (symbol.Declarations is not null && symbol.Declarations.Count > 0)
        {
            IDeclaration? declaration = null;
            foreach (var d in symbol.Declarations)
            {
                if (Utilities.GetNameOfDeclaration((Node)d) is not null)
                {
                    declaration = d;
                    break;
                }
            }
            var name = declaration is not null ? Utilities.GetNameOfDeclaration((Node)declaration) as Node : null;
            if (declaration is not null && name is not null)
            {
                if (NodeTests.IsCallExpression((Node)declaration) && Utilities.IsBindableObjectDefinePropertyCall((CallExpression)declaration))
                {
                    return Utilities.SymbolName(symbol);
                }
                if (NodeTests.IsComputedPropertyName(name) && (GetCheckFlags(symbol) & CheckFlags.Late) == 0)
                {
                    var nameType = GetSymbolLinks(symbol).NameType;
                    if (nameType is not null && (nameType.Flags & TypeFlags.StringOrNumberLiteral) != 0)
                    {
                        // Computed property name isn't late bound, but has a well-known name type - use name type to generate a symbol name
                        var result = GetNameOfSymbolFromNameType(symbol, context);
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                }
                return Utilities.DeclarationNameToString((Node)name);
            }
            if (declaration is null)
            {
                declaration = symbol.Declarations[0]; // Declaration may be nameless, but we'll try anyway
            }
            if (((Node)declaration).Parent is not null && ((Node)declaration).Parent!.Kind == SyntaxKind.VariableDeclaration)
            {
                return Utilities.DeclarationNameToString(((VariableDeclaration)((Node)declaration).Parent!).Name);
            }
            switch (declaration.Kind)
            {
                case SyntaxKind.ClassExpression:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                    if (context is not null && !context.EncounteredError && (context.Flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0)
                    {
                        context.EncounteredError = true;
                    }
                    return declaration.Kind == SyntaxKind.ClassExpression ? "(Anonymous class)" : "(Anonymous function)";
            }
        }
        var name2 = GetNameOfSymbolFromNameType(symbol, context);
        return name2 ?? Utilities.SymbolName(symbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:11324-11418 — <c>isDeclarationVisible</c>.</summary>
    public bool IsDeclarationVisible(Node? node)
    {
        if (node is not null)
        {
            var links = GetNodeLinks(node);
            if (links.IsVisible is null)
            {
                links.IsVisible = DetermineIfDeclarationIsVisible();
            }
            return links.IsVisible.Value;
        }

        return false;

        bool DetermineIfDeclarationIsVisible()
        {
            switch (node.Kind)
            {
                case SyntaxKind.JSDocCallbackTag:
                case SyntaxKind.JSDocTypedefTag:
                case SyntaxKind.JSDocEnumTag:
                    // Top-level jsdoc type aliases are considered exported
                    // First parent is comment node, second is hosting declaration or token; we only care about those tokens or declarations whose parent is a source file
                    return node.Parent is not null && node.Parent.Parent is not null && node.Parent.Parent.Parent is not null && NodeTests.IsSourceFile(node.Parent.Parent.Parent);
                case SyntaxKind.BindingElement:
                    return IsDeclarationVisible(node.Parent!.Parent);
                case SyntaxKind.VariableDeclaration:
                    if (Utilities.IsBindingPattern(((VariableDeclaration)node).Name))
                    {
                        var elementCount = ((VariableDeclaration)node).Name is ArrayBindingPattern arrayPattern
                            ? arrayPattern.Elements!.Count
                            : ((ObjectBindingPattern)((VariableDeclaration)node).Name!).Elements!.Count;
                        if (elementCount == 0)
                        {
                            // If the binding pattern is empty, this variable declaration is not visible
                            return false;
                        }
                    }
                    goto case SyntaxKind.ModuleDeclaration;
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                    // external module augmentation is always visible
                    if (Utilities.IsExternalModuleAugmentation(node))
                    {
                        return true;
                    }
                    var parent = GetDeclarationContainer(node);
                    // If the node is not exported or it is not ambient module element (except import declaration)
                    if (
                        (GetCombinedModifierFlagsCached((IDeclaration)node) & ModifierFlags.Export) == 0 &&
                        !(node.Kind != SyntaxKind.ImportEqualsDeclaration && parent.Kind != SyntaxKind.SourceFile && (parent.Flags & NodeFlags.Ambient) != 0)
                    )
                    {
                        return Utilities.IsGlobalSourceFile(parent);
                    }
                    // Exported members/ambient module elements (exception import declaration) are visible if parent is visible
                    return IsDeclarationVisible(parent);

                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    if (Utilities.HasEffectiveModifier(node, ModifierFlags.Private | ModifierFlags.Protected))
                    {
                        // Private/protected properties/methods are not visible
                        return false;
                    }
                    goto case SyntaxKind.Constructor;
                // Public properties/methods are visible if its parents are visible, so:
                case SyntaxKind.Constructor:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.CallSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.Parameter:
                case SyntaxKind.ModuleBlock:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.TypeLiteral:
                case SyntaxKind.TypeReference:
                case SyntaxKind.ArrayType:
                case SyntaxKind.TupleType:
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType:
                case SyntaxKind.ParenthesizedType:
                case SyntaxKind.NamedTupleMember:
                    return IsDeclarationVisible(node.Parent);

                // Default binding, import specifier and namespace import is visible
                // only on demand so by default it is not visible
                case SyntaxKind.ImportClause:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ImportSpecifier:
                    return false;

                // Type parameters are always visible
                // Source file and namespace export are always visible
                case SyntaxKind.TypeParameter:
                case SyntaxKind.SourceFile:
                case SyntaxKind.NamespaceExportDeclaration:
                    return true;

                // Export assignments do not create name bindings outside the module
                case SyntaxKind.ExportAssignment:
                    return false;

                default:
                    return false;
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1200-1211 — the <c>createPrinter*</c> family.</summary>
    /// <remarks>
    /// PORT-NOTE: loud, named blocker (docs/architecture.md §15.15 ruling 1). The printer lives in
    /// <c>emitter.ts:1200-1211</c> (<c>createPrinter</c> at :1211 plus the four configured wrappers at :1200-1209),
    /// which is Layer 7/M7 and is not ported. The four string converters build their node and then throw this instead of
    /// returning a string that would silently pollute the <c>.types</c>/<c>.symbols</c> baselines; M7 deletes the throw
    /// and calls the printer. Each message carries the reference line of the member. Pinned by
    /// <c>CheckerNodeBuilderTests</c>, and listed in docs §15.16's residual table.
    /// </remarks>
    private static NotSupportedException PrinterNotSupported(string member, int referenceLine)
        => new NotSupportedException(
            $"{member} (checker.ts:{referenceLine}) requires the printer (emitter.ts:1200-1211, createPrinter) — Layer 7/M7, "
            + "not ported. The type/signature node is built, but it cannot be written to a string yet; see "
            + "docs/architecture.md §15.15 ruling 1 and §15.16's residual table.");
}

/// <summary>ported from: src/compiler/types.ts:5684-5689 — <c>SymbolVisibilityResult</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares an interface and every producer writes an object literal; the port has no anonymous object
/// types, so this is the class the accessibility members construct. It is declared here (with its derived sibling) so
/// that a second slice does not invent a second one; the home is the checker, where every producer lives.
/// </remarks>
internal sealed class SymbolVisibilityResult : ISymbolVisibilityResult
{
    /// <summary>ported from: src/compiler/types.ts:5685 — <c>accessibility</c>.</summary>
    public SymbolAccessibility Accessibility { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5686 — <c>aliasesToMakeVisible</c>.</summary>
    public List<Node>? AliasesToMakeVisible { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5687 — <c>errorSymbolName</c>.</summary>
    public string? ErrorSymbolName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5688 — <c>errorNode</c>.</summary>
    public Node? ErrorNode { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:5679-5683 — <c>SymbolAccessibilityResult</c>.</summary>
internal sealed class SymbolAccessibilityResult : ISymbolAccessibilityResult
{
    /// <summary>ported from: src/compiler/types.ts:5680 — <c>accessibility</c>.</summary>
    public SymbolAccessibility Accessibility { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5681 — <c>aliasesToMakeVisible</c>.</summary>
    public List<Node>? AliasesToMakeVisible { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5682 — <c>errorSymbolName</c>.</summary>
    public string? ErrorSymbolName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5679 — <c>errorModuleName</c>.</summary>
    public string? ErrorModuleName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5683 — <c>errorNode</c>.</summary>
    public Node? ErrorNode { get; set; }
}
