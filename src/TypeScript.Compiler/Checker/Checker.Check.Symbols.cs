using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1407-1408 (AddUnusedDiagnostic)
/// <summary>
/// The callback <c>checkUnusedIdentifiers</c> reports through (checker.ts:1408).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this at checker.ts module scope (line 1408, beside <c>UnusedKind</c> at line 1402, which the
/// port keeps in <c>Checker/CheckerEnums.cs</c>), and it is the only shape the checker's unused-identifier block needs:
/// every caller passes an inline lambda, so no landed file had claimed the name. It is declared here because this file
/// declares the whole family that consumes it.
/// </remarks>
internal delegate void AddUnusedDiagnostic(Node containingNode, UnusedKind kind, Diagnostic diagnostic);

// ported from: src/compiler/checker.ts:2319 (allPotentiallyUnusedIdentifiers)
// ported from: src/compiler/checker.ts:2371-2372 (potentialUnusedRenamedBindingElementsInTypes, awaitedTypeStack)
// ported from: src/compiler/checker.ts:43057-43060 (isPrivateWithinAmbient)
// ported from: src/compiler/checker.ts:43061-43081 (getEffectiveDeclarationFlags)
// ported from: src/compiler/checker.ts:43083-43357 (checkFunctionOrConstructorSymbol, checkFunctionOrConstructorSymbolWorker)
// ported from: src/compiler/checker.ts:43358-43484 (checkExportsOnMergedDeclarations, checkExportsOnMergedDeclarationsWorker)
// ported from: src/compiler/checker.ts:43486-43583 (getAwaitedTypeOfPromise, getPromisedTypeOfPromise)
// ported from: src/compiler/checker.ts:43585-43836 (checkAwaitedType … getAwaitedTypeNoAlias)
// ported from: src/compiler/checker.ts:43838-43935 (checkAsyncFunctionReturnType)
// ported from: src/compiler/checker.ts:43937-44221 (checkGrammarDecorator … checkFunctionDeclaration)
// ported from: src/compiler/checker.ts:44223-44345 (the checkJSDoc* family, getIdentifierFromEntityNameExpression)
// ported from: src/compiler/checker.ts:44347-44434 (checkFunctionOrMethodDeclaration, registerForUnusedIdentifiersCheck)
// ported from: src/compiler/checker.ts:44438-44761 (checkUnusedIdentifiers … importClauseFromImported)
/// <summary>
/// The declaration checks the checker defers: overload agreement over merged function/constructor symbols, the exported
/// declaration spaces of merged declarations, the awaited-type family, decorator checks, the JSDoc tag checks, and the
/// whole unused-identifier (locals, parameters, type parameters, imports, class members) machinery.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference states several of its parameters as node unions (the <c>SignatureDeclaration</c>,
/// <c>Expression</c>, <c>TypeNode</c>, <c>BindingPattern</c>, <c>ClassLikeDeclaration</c> and
/// <c>HasLocals</c>/<c>PotentialUnusedIdentifier</c> aliases). The port has no C# type for those aliases, so each is
/// spelled with <see cref="Node"/> (or <see cref="IDeclaration"/>, where the reference asks for a <c>Declaration</c>)
/// and the slot the reference reads off the union is read through the concrete class or the capability interface the
/// port's model gives it (<see cref="INamedDeclaration"/> for <c>name</c>, <see cref="ILocalsContainer"/> for
/// <c>locals</c>, <see cref="IClassLikeDeclarationBase"/> for <c>members</c>, and a small switch where the union's arms
/// disagree on the slot's type).
/// </para>
/// <para>
/// PORT-NOTE: TS's anonymous <c>{ value?: Type }</c> out-holder of <c>getPromisedTypeOfPromise</c> is
/// <see cref="StrongBox{T}"/> — the port's existing shape for "the callee writes back through an object"
/// (<c>Parser/ParenthesizerRules.cs</c> uses it for the same reason).
/// </para>
/// <para>
/// PORT-NOTE: the three slots this band reads out of the checker's state block (checker.ts:2319, :2371-2372) are
/// declared by that block's owner, <c>Checker/Checker.Initialize.cs</c>, and every use here is unqualified, as for any
/// other <c>Checker</c> slot. The three grouping maps <c>checkUnusedLocalsAndParameters</c> builds are locals of that
/// member (see its own note).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Declaration flags and overload agreement (checker.ts:43057-43357)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:43057-43060 — <c>isPrivateWithinAmbient</c>.</summary>
    internal static bool IsPrivateWithinAmbient(Node node)
        => (Utilities.HasEffectiveModifier(node, ModifierFlags.Private) || Utilities.IsPrivateIdentifierClassElementDeclaration(node))
            && (node.Flags & NodeFlags.Ambient) != 0;

    /// <summary>ported from: src/compiler/checker.ts:43061-43081 — <c>getEffectiveDeclarationFlags</c>.</summary>
    internal ModifierFlags GetEffectiveDeclarationFlags(IDeclaration n, ModifierFlags flagsToCheck)
    {
        var flags = GetCombinedModifierFlagsCached(n);
        var parent = ((Node)n).Parent!;

        // children of classes (even ambient classes) should not be marked as ambient or export
        // because those flags have no useful semantics there.
        if (parent.Kind != SyntaxKind.InterfaceDeclaration
            && parent.Kind != SyntaxKind.ClassDeclaration
            && parent.Kind != SyntaxKind.ClassExpression
            && (n.Flags & NodeFlags.Ambient) != 0)
        {
            var container = Utilities.GetEnclosingContainer((Node)n);
            if (container is not null && (container.Flags & NodeFlags.ExportContext) != 0
                && (flags & ModifierFlags.Ambient) == 0
                && !(NodeTests.IsModuleBlock(parent) && NodeTests.IsModuleDeclaration(parent.Parent!) && Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)parent.Parent!)))
            {
                // It is nested in an ambient export context, which means it is automatically exported
                flags |= ModifierFlags.Export;
            }
            flags |= ModifierFlags.Ambient;
        }

        return flags & flagsToCheck;
    }

    /// <summary>ported from: src/compiler/checker.ts:43083-43085 — <c>checkFunctionOrConstructorSymbol</c>.</summary>
    internal void CheckFunctionOrConstructorSymbol(ISymbol symbol)
        => AddLazyDiagnostic(() => CheckFunctionOrConstructorSymbolWorker(symbol));

    /// <summary>ported from: src/compiler/checker.ts:43087-43357 — <c>checkFunctionOrConstructorSymbolWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's three nested helpers take the worker's own locals under the reference's names
    /// (<c>flagsToCheck</c>, <c>someHaveQuestionToken</c>, <c>allHaveQuestionToken</c>). C# forbids a nested declaration
    /// from reusing a name declared in an enclosing scope, so those three parameters are spelled <c>flags</c>,
    /// <c>someHaveQuestion</c> and <c>allHaveQuestion</c> here; every other name, capture and call is the reference's.
    /// </remarks>
    internal void CheckFunctionOrConstructorSymbolWorker(ISymbol symbol)
    {
        IDeclaration GetCanonicalOverload(IList<IDeclaration> overloads, Node? implementation)
        {
            // Consider the canonical set of flags to be the flags of the bodyDeclaration or the first declaration
            // Error on all deviations from this canonical set of flags
            // The caveat is that if some overloads are defined in lib.d.ts, we don't want to
            // report the errors on those. To achieve this, we will say that the implementation is
            // the canonical signature only if it is in the same container as the first overload
            var implementationSharesContainerWithFirstOverload = implementation is not null && ReferenceEquals(implementation.Parent, ((Node)overloads[0]).Parent);
            return implementationSharesContainerWithFirstOverload ? (IDeclaration)implementation! : overloads[0];
        }

        void CheckFlagAgreementBetweenOverloads(IList<IDeclaration> overloads, Node? implementation, ModifierFlags flags, ModifierFlags someOverloadFlags, ModifierFlags allOverloadFlags)
        {
            // Error if some overloads have a flag that is not shared by all overloads. To find the
            // deviations, we XOR someOverloadFlags with allOverloadFlags
            var someButNotAllOverloadFlags = someOverloadFlags ^ allOverloadFlags;
            if (someButNotAllOverloadFlags != 0)
            {
                var canonicalFlags = GetEffectiveDeclarationFlags(GetCanonicalOverload(overloads, implementation), flags);
                var groups = Core.Group(overloads, (IDeclaration o) => Utilities.GetSourceFileOfNode((Node)o)!.FileName ?? "", (IList<IDeclaration> files) => files);
                foreach (var overloadsInFile in groups)
                {
                    var canonicalFlagsForFile = GetEffectiveDeclarationFlags(GetCanonicalOverload(overloadsInFile, implementation), flags);
                    foreach (var o in overloadsInFile)
                    {
                        var deviation = GetEffectiveDeclarationFlags(o, flags) ^ canonicalFlags;
                        var deviationInFile = GetEffectiveDeclarationFlags(o, flags) ^ canonicalFlagsForFile;
                        if ((deviationInFile & ModifierFlags.Export) != 0)
                        {
                            // Overloads in different files need not all have export modifiers. This is ok:
                            //   // lib.d.ts
                            //   declare function foo(s: number): string;
                            //   declare function foo(s: string): number;
                            //   export { foo };
                            //
                            //   // app.ts
                            //   declare module "lib" {
                            //     export function foo(s: boolean): boolean;
                            //   }
                            Error((Node?)Utilities.GetNameOfDeclaration((Node)o), Diagnostics.Overload_signatures_must_all_be_exported_or_non_exported);
                        }
                        else if ((deviationInFile & ModifierFlags.Ambient) != 0)
                        {
                            // Though rare, a module augmentation (necessarily ambient) is allowed to add overloads
                            // to a non-ambient function in an implementation file.
                            Error((Node?)Utilities.GetNameOfDeclaration((Node)o), Diagnostics.Overload_signatures_must_all_be_ambient_or_non_ambient);
                        }
                        else if ((deviation & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                        {
                            Error((Node?)Utilities.GetNameOfDeclaration((Node)o) ?? (Node)o, Diagnostics.Overload_signatures_must_all_be_public_private_or_protected);
                        }
                        else if ((deviation & ModifierFlags.Abstract) != 0)
                        {
                            Error((Node?)Utilities.GetNameOfDeclaration((Node)o), Diagnostics.Overload_signatures_must_all_be_abstract_or_non_abstract);
                        }
                    }
                }
            }
        }

        void CheckQuestionTokenAgreementBetweenOverloads(IList<IDeclaration> overloads, Node? implementation, bool someHaveQuestion, bool allHaveQuestion)
        {
            if (someHaveQuestion != allHaveQuestion)
            {
                var canonicalHasQuestionToken = Utilities.HasQuestionToken((Node)GetCanonicalOverload(overloads, implementation));
                foreach (var o in overloads)
                {
                    var deviation = Utilities.HasQuestionToken((Node)o) != canonicalHasQuestionToken;
                    if (deviation)
                    {
                        Error((Node?)Utilities.GetNameOfDeclaration((Node)o), Diagnostics.Overload_signatures_must_all_be_optional_or_required);
                    }
                }
            }
        }

        var flagsToCheck = ModifierFlags.Export | ModifierFlags.Ambient | ModifierFlags.Private | ModifierFlags.Protected | ModifierFlags.Abstract;
        var someNodeFlags = ModifierFlags.None;
        var allNodeFlags = flagsToCheck;
        var someHaveQuestionToken = false;
        var allHaveQuestionToken = true;
        var hasOverloads = false;
        Node? bodyDeclaration = null;
        Node? lastSeenNonAmbientDeclaration = null;
        Node? previousDeclaration = null;

        var declarations = symbol.Declarations;
        var isConstructor = (symbol.Flags & SymbolFlags.Constructor) != 0;

        void ReportImplementationExpectedError(Node declarationNode)
        {
            if (((INamedDeclaration)declarationNode).Name is not null && Utilities.NodeIsMissing(((INamedDeclaration)declarationNode).Name))
            {
                return;
            }

            var seen = false;
            var subsequentNode = ForEachChildTable.ForEachChild(declarationNode.Parent!, (Node c) =>
            {
                if (seen)
                {
                    return c;
                }

                seen = ReferenceEquals(c, declarationNode);
                return null;
            });
            // We may be here because of some extra nodes between overloads that could not be parsed into a valid node.
            // In this case the subsequent node is not really consecutive (.pos !== node.end), and we must ignore it here.
            if (subsequentNode is not null && subsequentNode.Pos == declarationNode.End)
            {
                if (subsequentNode.Kind == declarationNode.Kind)
                {
                    var errorNode = ((INamedDeclaration)subsequentNode).Name ?? subsequentNode;
                    var subsequentName = ((INamedDeclaration)subsequentNode).Name;
                    var declarationName = ((INamedDeclaration)declarationNode).Name;
                    if (
                        declarationName is not null && subsequentName is not null && (
                            // both are private identifiers
                            NodeTests.IsPrivateIdentifier(declarationName) && NodeTests.IsPrivateIdentifier(subsequentName) && ((PrivateIdentifier)declarationName).EscapedText == ((PrivateIdentifier)subsequentName).EscapedText ||
                            // Both are computed property names
                            NodeTests.IsComputedPropertyName(declarationName) && NodeTests.IsComputedPropertyName(subsequentName) && IsTypeIdenticalTo(CheckComputedPropertyName((ComputedPropertyName)declarationName), CheckComputedPropertyName((ComputedPropertyName)subsequentName)) ||
                            // Both are literal property names that are the same.
                            Utilities.IsPropertyNameLiteral(declarationName) && Utilities.IsPropertyNameLiteral(subsequentName) &&
                                Utilities.GetEscapedTextOfIdentifierOrLiteral(declarationName) == Utilities.GetEscapedTextOfIdentifierOrLiteral(subsequentName)
                        )
                    )
                    {
                        var reportError = (declarationNode.Kind == SyntaxKind.MethodDeclaration || declarationNode.Kind == SyntaxKind.MethodSignature) &&
                            Utilities.IsStatic(declarationNode) != Utilities.IsStatic(subsequentNode);
                        // we can get here in two cases
                        // 1. mixed static and instance class members
                        // 2. something with the same name was defined before the set of overloads that prevents them from merging
                        // here we'll report error only for the first case since for second we should already report error in binder
                        if (reportError)
                        {
                            var diagnostic = Utilities.IsStatic(declarationNode) ? Diagnostics.Function_overload_must_be_static : Diagnostics.Function_overload_must_not_be_static;
                            Error(errorNode, diagnostic);
                        }
                        return;
                    }
                    if (Utilities.NodeIsPresent((Node?)((IFunctionLikeDeclarationBase)subsequentNode).Body))
                    {
                        Error(errorNode, Diagnostics.Function_implementation_name_must_be_0, Utilities.DeclarationNameToString(((INamedDeclaration)declarationNode).Name!));
                        return;
                    }
                }
            }
            var finalErrorNode = ((INamedDeclaration)declarationNode).Name ?? declarationNode;
            if (isConstructor)
            {
                Error(finalErrorNode, Diagnostics.Constructor_implementation_is_missing);
            }
            else
            {
                // Report different errors regarding non-consecutive blocks of declarations depending on whether
                // the node in question is abstract.
                if (Utilities.HasSyntacticModifier(declarationNode, ModifierFlags.Abstract))
                {
                    Error(finalErrorNode, Diagnostics.All_declarations_of_an_abstract_method_must_be_consecutive);
                }
                else
                {
                    Error(finalErrorNode, Diagnostics.Function_implementation_is_missing_or_not_immediately_following_the_declaration);
                }
            }
        }

        var duplicateFunctionDeclaration = false;
        var multipleConstructorImplementation = false;
        var hasNonAmbientClass = false;
        var functionDeclarations = new List<IDeclaration>();
        if (declarations is not null)
        {
            foreach (var current in declarations)
            {
                var node = (Node)current;
                var inAmbientContext = (node.Flags & NodeFlags.Ambient) != 0;
                var inAmbientContextOrInterface = node.Parent is not null && (node.Parent.Kind == SyntaxKind.InterfaceDeclaration || node.Parent.Kind == SyntaxKind.TypeLiteral) || inAmbientContext;
                if (inAmbientContextOrInterface)
                {
                    // check if declarations are consecutive only if they are non-ambient
                    // 1. ambient declarations can be interleaved
                    // i.e. this is legal
                    //     declare function foo();
                    //     declare function bar();
                    //     declare function foo();
                    // 2. mixing ambient and non-ambient declarations is a separate error that will be reported - do not want to report an extra one
                    previousDeclaration = null;
                }

                if ((node.Kind == SyntaxKind.ClassDeclaration || node.Kind == SyntaxKind.ClassExpression) && !inAmbientContext)
                {
                    hasNonAmbientClass = true;
                }

                if (node.Kind == SyntaxKind.FunctionDeclaration || node.Kind == SyntaxKind.MethodDeclaration || node.Kind == SyntaxKind.MethodSignature || node.Kind == SyntaxKind.Constructor)
                {
                    functionDeclarations.Add(current);
                    var currentNodeFlags = GetEffectiveDeclarationFlags(current, flagsToCheck);
                    someNodeFlags |= currentNodeFlags;
                    allNodeFlags &= currentNodeFlags;
                    someHaveQuestionToken = someHaveQuestionToken || Utilities.HasQuestionToken(node);
                    allHaveQuestionToken = allHaveQuestionToken && Utilities.HasQuestionToken(node);
                    var bodyIsPresent = Utilities.NodeIsPresent((Node?)((IFunctionLikeDeclarationBase)node).Body);

                    if (bodyIsPresent && bodyDeclaration is not null)
                    {
                        if (isConstructor)
                        {
                            multipleConstructorImplementation = true;
                        }
                        else
                        {
                            duplicateFunctionDeclaration = true;
                        }
                    }
                    else if (previousDeclaration is not null && ReferenceEquals(previousDeclaration.Parent, node.Parent) && previousDeclaration.End != node.Pos)
                    {
                        ReportImplementationExpectedError(previousDeclaration);
                    }

                    if (bodyIsPresent)
                    {
                        bodyDeclaration ??= node;
                    }
                    else
                    {
                        hasOverloads = true;
                    }

                    previousDeclaration = node;

                    if (!inAmbientContextOrInterface)
                    {
                        lastSeenNonAmbientDeclaration = node;
                    }
                }
                if (Utilities.IsInJSFile((Node)current) && Utilities.IsFunctionLike((Node)current) && ((IJSDocContainer)current).JsDoc is not null)
                {
                    hasOverloads = Core.Length(Utilities.GetJSDocOverloadTags(node)) > 0;
                }
            }
        }

        if (multipleConstructorImplementation)
        {
            foreach (var declaration in functionDeclarations)
            {
                Error((Node)declaration, Diagnostics.Multiple_constructor_implementations_are_not_allowed);
            }
        }

        if (duplicateFunctionDeclaration)
        {
            foreach (var declaration in functionDeclarations)
            {
                Error((Node?)Utilities.GetNameOfDeclaration((Node)declaration) ?? (Node)declaration, Diagnostics.Duplicate_function_implementation);
            }
        }

        if (hasNonAmbientClass && !isConstructor && (symbol.Flags & SymbolFlags.Function) != 0 && declarations is not null)
        {
            var relatedDiagnostics = Core.Map(
                Core.Filter(declarations, (IDeclaration d) => d.Kind == SyntaxKind.ClassDeclaration),
                (IDeclaration d, int _) => Utilities.CreateDiagnosticForNode((Node)d, Diagnostics.Consider_adding_a_declare_modifier_to_this_class))!;

            foreach (var declaration in declarations)
            {
                var diagnostic = declaration.Kind == SyntaxKind.ClassDeclaration
                    ? Diagnostics.Class_declaration_cannot_implement_overload_list_for_0
                    : declaration.Kind == SyntaxKind.FunctionDeclaration
                        ? Diagnostics.Function_with_bodies_can_only_merge_with_classes_that_are_ambient
                        : null;
                if (diagnostic is not null)
                {
                    Utilities.AddRelatedInfo(
                        Error((Node?)Utilities.GetNameOfDeclaration((Node)declaration) ?? (Node)declaration, diagnostic, Utilities.SymbolName(symbol)),
                        [.. relatedDiagnostics]);
                }
            }
        }

        // Abstract methods can't have an implementation -- in particular, they don't need one.
        if (
            lastSeenNonAmbientDeclaration is not null && ((IFunctionLikeDeclarationBase)lastSeenNonAmbientDeclaration).Body is null
            && !Utilities.HasSyntacticModifier(lastSeenNonAmbientDeclaration, ModifierFlags.Abstract) && !Utilities.HasQuestionToken(lastSeenNonAmbientDeclaration)
        )
        {
            ReportImplementationExpectedError(lastSeenNonAmbientDeclaration);
        }

        if (hasOverloads)
        {
            if (declarations is not null)
            {
                CheckFlagAgreementBetweenOverloads(declarations, bodyDeclaration, flagsToCheck, someNodeFlags, allNodeFlags);
                CheckQuestionTokenAgreementBetweenOverloads(declarations, bodyDeclaration, someHaveQuestionToken, allHaveQuestionToken);
            }

            if (bodyDeclaration is not null)
            {
                var signatures = GetSignaturesOfSymbol(symbol);
                var bodySignature = GetSignatureFromDeclaration(bodyDeclaration);
                foreach (var signature in signatures)
                {
                    if (!IsImplementationCompatibleWithOverload(bodySignature, signature))
                    {
                        var errorNode = signature.Declaration is not null && NodeTests.IsJSDocSignature(signature.Declaration)
                            ? ((JSDocTag)signature.Declaration.Parent!).TagName
                            : signature.Declaration;
                        Utilities.AddRelatedInfo(
                            Error(errorNode, Diagnostics.This_overload_signature_is_not_compatible_with_its_implementation_signature),
                            Utilities.CreateDiagnosticForNode(bodyDeclaration, Diagnostics.The_implementation_signature_is_declared_here));
                        break;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Exported declaration spaces of merged declarations (checker.ts:43358-43484)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:43358-43360 — <c>checkExportsOnMergedDeclarations</c>.</summary>
    internal void CheckExportsOnMergedDeclarations(IDeclaration node)
        => AddLazyDiagnostic(() => CheckExportsOnMergedDeclarationsWorker(node));

    /// <summary>ported from: src/compiler/checker.ts:43362-43484 — <c>checkExportsOnMergedDeclarationsWorker</c>.</summary>
    internal void CheckExportsOnMergedDeclarationsWorker(IDeclaration node)
    {
        // if localSymbol is defined on node then node itself is exported - check is required
        var symbol = node.LocalSymbol;
        if (symbol is null)
        {
            // local symbol is undefined => this declaration is non-exported.
            // however symbol might contain other declarations that are exported
            symbol = GetSymbolOfDeclaration(node);
            if (symbol.ExportSymbol is null)
            {
                // this is a pure local symbol (all declarations are non-exported) - no need to check anything
                return;
            }
        }

        // run the check only for the first declaration in the list
        if (!ReferenceEquals(Utilities.GetDeclarationOfKind(symbol, node.Kind), node))
        {
            return;
        }

        var exportedDeclarationSpaces = DeclarationSpaces.None;
        var nonExportedDeclarationSpaces = DeclarationSpaces.None;
        var defaultExportedDeclarationSpaces = DeclarationSpaces.None;
        foreach (var d in symbol.Declarations!)
        {
            var declarationSpaces = GetDeclarationSpaces(d);
            var effectiveDeclarationFlags = GetEffectiveDeclarationFlags(d, ModifierFlags.Export | ModifierFlags.Default);

            if ((effectiveDeclarationFlags & ModifierFlags.Export) != 0)
            {
                if ((effectiveDeclarationFlags & ModifierFlags.Default) != 0)
                {
                    defaultExportedDeclarationSpaces |= declarationSpaces;
                }
                else
                {
                    exportedDeclarationSpaces |= declarationSpaces;
                }
            }
            else
            {
                nonExportedDeclarationSpaces |= declarationSpaces;
            }
        }

        // Spaces for anything not declared a 'default export'.
        var nonDefaultExportedDeclarationSpaces = exportedDeclarationSpaces | nonExportedDeclarationSpaces;

        var commonDeclarationSpacesForExportsAndLocals = exportedDeclarationSpaces & nonExportedDeclarationSpaces;
        var commonDeclarationSpacesForDefaultAndNonDefault = defaultExportedDeclarationSpaces & nonDefaultExportedDeclarationSpaces;

        if (commonDeclarationSpacesForExportsAndLocals != DeclarationSpaces.None || commonDeclarationSpacesForDefaultAndNonDefault != DeclarationSpaces.None)
        {
            // declaration spaces for exported and non-exported declarations intersect
            foreach (var d in symbol.Declarations!)
            {
                var declarationSpaces = GetDeclarationSpaces(d);

                var name = (Node?)Utilities.GetNameOfDeclaration((Node)d);
                // Only error on the declarations that contributed to the intersecting spaces.
                if ((declarationSpaces & commonDeclarationSpacesForDefaultAndNonDefault) != 0)
                {
                    Error(name, Diagnostics.Merged_declaration_0_cannot_include_a_default_export_declaration_Consider_adding_a_separate_export_default_0_declaration_instead, Utilities.DeclarationNameToString(name!));
                }
                else if ((declarationSpaces & commonDeclarationSpacesForExportsAndLocals) != 0)
                {
                    Error(name, Diagnostics.Individual_declarations_in_merged_declaration_0_must_be_all_exported_or_all_local, Utilities.DeclarationNameToString(name!));
                }
            }
        }

        DeclarationSpaces GetDeclarationSpaces(IDeclaration decl)
        {
            var d = (Node)decl;
            switch (d.Kind)
            {
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.TypeAliasDeclaration:

                // A jsdoc typedef and callback are, by definition, type aliases.
                // falls through
                case SyntaxKind.JSDocTypedefTag:
                case SyntaxKind.JSDocCallbackTag:
                case SyntaxKind.JSDocEnumTag:
                    return DeclarationSpaces.ExportType;
                case SyntaxKind.ModuleDeclaration:
                    return Utilities.IsAmbientModule(d) || Binder.GetModuleInstanceState((ModuleDeclaration)d) != ModuleInstanceState.NonInstantiated
                        ? DeclarationSpaces.ExportNamespace | DeclarationSpaces.ExportValue
                        : DeclarationSpaces.ExportNamespace;
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.EnumMember:
                    return DeclarationSpaces.ExportType | DeclarationSpaces.ExportValue;
                case SyntaxKind.SourceFile:
                    return DeclarationSpaces.ExportType | DeclarationSpaces.ExportValue | DeclarationSpaces.ExportNamespace;
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.BinaryExpression:
                    {
                        var exportNode = d;
                        var expression = NodeTests.IsExportAssignment(exportNode) ? ((ExportAssignment)exportNode).Expression : ((BinaryExpression)exportNode).Right;
                        // Export assigned entity name expressions act as aliases and should fall through, otherwise they export values
                        if (!Utilities.IsEntityNameExpression(expression!))
                        {
                            return DeclarationSpaces.ExportValue;
                        }
                        d = (Node)expression!;
                        goto case SyntaxKind.ImportEqualsDeclaration;
                    }

                // The below options all declare an Alias, which is allowed to merge with other values within the importing module.
                // falls through
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ImportClause:
                    {
                        var result = DeclarationSpaces.None;
                        var target = ResolveAlias(GetSymbolOfDeclaration((IDeclaration)d));
                        foreach (var inner in target.Declarations!)
                        {
                            result |= GetDeclarationSpaces(inner);
                        }
                        return result;
                    }
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.ImportSpecifier: // https://github.com/Microsoft/TypeScript/pull/7591
                case SyntaxKind.Identifier: // https://github.com/microsoft/TypeScript/issues/36098
                    // Identifiers are used as declarations of assignment declarations whose parents may be
                    // SyntaxKind.CallExpression - `Object.defineProperty(thing, "aField", {value: 42});`
                    // SyntaxKind.ElementAccessExpression - `thing["aField"] = 42;` or `thing["aField"];` (with a doc comment on it)
                    // or SyntaxKind.PropertyAccessExpression - `thing.aField = 42;`
                    // all of which are pretty much always values, or at least imply a value meaning.
                    // It may be apprpriate to treat these as aliases in the future.
                    return DeclarationSpaces.ExportValue;
                case SyntaxKind.MethodSignature:
                case SyntaxKind.PropertySignature:
                    return DeclarationSpaces.ExportType;
                default:
                    // PORT-NOTE: TS ends with `Debug.failBadSyntaxKind(d)`, which returns `never`; the port's
                    // `Debug.FailBadSyntaxKind` is void, so the value-producing form is `Debug.Fail<T>` (the shape
                    // Checker.IndexAccess.cs and Checker.SymbolSupport.cs use for the same situation).
                    return Debug.Fail<DeclarationSpaces>("Unexpected node kind: " + Debug.FormatSyntaxKind(d.Kind));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The awaited type family (checker.ts:43486-43836)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:43486-43489 — <c>getAwaitedTypeOfPromise</c>.</summary>
    internal IType? GetAwaitedTypeOfPromise(IType type, Node? errorNode = null, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
    {
        var promisedType = GetPromisedTypeOfPromise(type, errorNode);
        return promisedType is not null ? GetAwaitedType(promisedType, errorNode, diagnosticMessage, args) : null;
    }

    /// <summary>
    /// Gets the "promised type" of a promise.
    /// </summary>
    /// <remarks>
    /// <para>ported from: src/compiler/checker.ts:43496-43583 — <c>getPromisedTypeOfPromise</c>.</para>
    /// <para>
    /// PORT-NOTE: TS's third parameter is the anonymous out-holder <c>{ value?: Type }</c>; the port states it as
    /// <see cref="StrongBox{T}"/> of <see cref="IType"/> and reads its <c>Value</c> where the reference reads
    /// <c>.value</c>.
    /// </para>
    /// </remarks>
    internal IType? GetPromisedTypeOfPromise(IType type, Node? errorNode = null, StrongBox<IType?>? thisTypeForErrorOut = null)
    {
        //
        //  { // type
        //      then( // thenFunction
        //          onfulfilled: ( // onfulfilledParameterType
        //              value: T // valueParameterType
        //          ) => any
        //      ): any;
        //  }
        //

        if (IsTypeAny(type))
        {
            return null;
        }

        var typeAsPromise = (UnionOrIntersectionType)type;
        if (typeAsPromise.PromisedTypeOfPromise is not null)
        {
            return typeAsPromise.PromisedTypeOfPromise;
        }

        if (IsReferenceToType(type, GetGlobalPromiseType(/*reportErrors*/ false)))
        {
            var typeArguments = GetTypeArguments((ITypeReference)type);
            return typeAsPromise.PromisedTypeOfPromise = typeArguments[0];
        }

        // primitives with a `{ then() }` won't be unwrapped/adopted.
        if (AllTypesAssignableToKind(GetBaseConstraintOrType(type), TypeFlags.Primitive | TypeFlags.Never))
        {
            return null;
        }

        var thenFunction = GetTypeOfPropertyOfType(type, "then")!; // TODO: GH#18217
        if (IsTypeAny(thenFunction))
        {
            return null;
        }

        var thenSignatures = thenFunction is not null ? GetSignaturesOfType(thenFunction, SignatureKind.Call) : [];
        if (thenSignatures.Count == 0)
        {
            if (errorNode is not null)
            {
                Error(errorNode, Diagnostics.A_promise_must_have_a_then_method);
            }
            return null;
        }

        IType? thisTypeForError = null;
        List<ISignature>? candidates = null;
        foreach (var thenSignature in thenSignatures)
        {
            var thisType = GetThisTypeOfSignature(thenSignature);
            if (thisType is not null && !ReferenceEquals(thisType, _voidType) && !IsTypeRelatedTo(type, thisType, _subtypeRelation))
            {
                thisTypeForError = thisType;
            }
            else
            {
                candidates = Core.Append(candidates, thenSignature) as List<ISignature>;
            }
        }

        if (candidates is null)
        {
            Debug.AssertIsDefined(thisTypeForError);
            if (thisTypeForErrorOut is not null)
            {
                thisTypeForErrorOut.Value = thisTypeForError;
            }
            if (errorNode is not null)
            {
                Error(errorNode, Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1, TypeToString(type), TypeToString(thisTypeForError!));
            }
            return null;
        }

        var onfulfilledParameterType = GetTypeWithFacts(GetUnionType(Core.Map(candidates, (ISignature s, int _) => GetTypeOfFirstParameterOfSignature(s))!), TypeFacts.NEUndefinedOrNull);
        if (IsTypeAny(onfulfilledParameterType))
        {
            return null;
        }

        var onfulfilledParameterSignatures = GetSignaturesOfType(onfulfilledParameterType, SignatureKind.Call);
        if (onfulfilledParameterSignatures.Count == 0)
        {
            if (errorNode is not null)
            {
                Error(errorNode, Diagnostics.The_first_parameter_of_the_then_method_of_a_promise_must_be_a_callback);
            }
            return null;
        }

        return typeAsPromise.PromisedTypeOfPromise = GetUnionType(Core.Map(onfulfilledParameterSignatures, (ISignature s, int _) => GetTypeOfFirstParameterOfSignature(s))!, UnionReduction.Subtype);
    }

    /// <summary>
    /// Gets the "awaited type" of a type.
    /// </summary>
    /// <remarks>
    /// <para>ported from: src/compiler/checker.ts:43585-43591 — <c>checkAwaitedType</c>. When <paramref name="withAlias"/>
    /// is true the "awaited type" is wrapped in <c>Awaited&lt;T&gt;</c> if needed.</para>
    /// </remarks>
    internal IType CheckAwaitedType(IType type, bool withAlias, Node errorNode, DiagnosticMessage diagnosticMessage, params object?[] args)
    {
        var awaitedType = withAlias
            ? GetAwaitedType(type, errorNode, diagnosticMessage, args)
            : GetAwaitedTypeNoAlias(type, errorNode, diagnosticMessage, args);
        return awaitedType ?? _errorType;
    }

    /// <summary>Determines whether a type is an object with a callable `then` member.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:43595-43602 — <c>isThenableType</c>.</remarks>
    internal bool IsThenableType(IType type)
    {
        if (AllTypesAssignableToKind(GetBaseConstraintOrType(type), TypeFlags.Primitive | TypeFlags.Never))
        {
            // primitive types cannot be considered "thenable" since they are not objects.
            return false;
        }

        var thenFunction = GetTypeOfPropertyOfType(type, "then");
        return thenFunction is not null && GetSignaturesOfType(GetTypeWithFacts(thenFunction, TypeFacts.NEUndefinedOrNull), SignatureKind.Call).Count > 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:43611-43616 — <c>isAwaitedTypeInstantiation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's predicate narrows to the <c>AwaitedTypeInstantiation</c> interface it declares immediately above
    /// (checker.ts:43605-43609); the port answers <c>bool</c> because C# cannot narrow through a predicate and the
    /// interface adds nothing to a type that already carries <c>aliasSymbol</c>/<c>aliasTypeArguments</c>
    /// (<c>Checker.Relations.Entries.cs</c>/<c>Checker.Conditional.cs</c> read those slots directly).
    /// </remarks>
    internal bool IsAwaitedTypeInstantiation(IType type)
    {
        if ((type.Flags & TypeFlags.Conditional) != 0)
        {
            var awaitedSymbol = GetGlobalAwaitedSymbol(/*reportErrors*/ false);
            return awaitedSymbol is not null && ReferenceEquals(type.AliasSymbol, awaitedSymbol) && type.AliasTypeArguments?.Count == 1;
        }
        return false;
    }

    /// <summary>For a generic `Awaited&lt;T&gt;`, gets `T`.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:43622-43626 — <c>unwrapAwaitedType</c>.</remarks>
    internal IType UnwrapAwaitedType(IType type)
        => (type.Flags & TypeFlags.Union) != 0 ? MapType(type, t => UnwrapAwaitedType(t))!
            : IsAwaitedTypeInstantiation(type) ? type.AliasTypeArguments![0]
            : type;

    /// <summary>ported from: src/compiler/checker.ts:43628-43649 — <c>isAwaitedTypeNeeded</c>.</summary>
    internal bool IsAwaitedTypeNeeded(IType type)
    {
        // If this is already an `Awaited<T>`, we shouldn't wrap it. This helps to avoid `Awaited<Awaited<T>>` in higher-order.
        if (IsTypeAny(type) || IsAwaitedTypeInstantiation(type))
        {
            return false;
        }

        // We only need `Awaited<T>` if `T` contains possibly non-primitive types.
        if (IsGenericObjectType(type))
        {
            var baseConstraint = GetBaseConstraintOfType(type);
            // We only need `Awaited<T>` if `T` is a type variable that has no base constraint, or the base constraint of `T` is `any`, `unknown`, `{}`, `object`,
            // or is promise-like.
            if (baseConstraint is not null
                ? (baseConstraint.Flags & TypeFlags.AnyOrUnknown) != 0 || IsEmptyObjectType(baseConstraint) || SomeType(baseConstraint, IsThenableType)
                : MaybeTypeOfKind(type, TypeFlags.TypeVariable))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:43651-43661 — <c>tryCreateAwaitedType</c>.</summary>
    internal IType? TryCreateAwaitedType(IType type)
    {
        // Nothing to do if `Awaited<T>` doesn't exist
        var awaitedSymbol = GetGlobalAwaitedSymbol(/*reportErrors*/ true);
        if (awaitedSymbol is not null)
        {
            // Unwrap unions that may contain `Awaited<T>`, otherwise its possible to manufacture an `Awaited<Awaited<T> | U>` where
            // an `Awaited<T | U>` would suffice.
            return GetTypeAliasInstantiation(awaitedSymbol, [UnwrapAwaitedType(type)]);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:43663-43680 — <c>createAwaitedTypeIfNeeded</c>.</summary>
    internal IType CreateAwaitedTypeIfNeeded(IType type)
    {
        // We wrap type `T` in `Awaited<T>` based on the following conditions:
        // - `T` is not already an `Awaited<U>`, and
        // - `T` is generic, and
        // - One of the following applies:
        //   - `T` has no base constraint, or
        //   - The base constraint of `T` is `any`, `unknown`, `object`, or `{}`, or
        //   - The base constraint of `T` is an object type with a callable `then` method.

        if (IsAwaitedTypeNeeded(type))
        {
            return TryCreateAwaitedType(type) ?? type;
        }

        Debug.Assert(IsAwaitedTypeInstantiation(type) || GetPromisedTypeOfPromise(type) is null, "type provided should not be a non-generic 'promise'-like.");
        return type;
    }

    /// <summary>
    /// Gets the "awaited type" of a type.
    /// </summary>
    /// <remarks>
    /// <para>ported from: src/compiler/checker.ts:43690-43693 — <c>getAwaitedType</c>.</para>
    /// <para>
    /// The "awaited type" of an expression is its "promised type" if the expression is a Promise-like type; otherwise,
    /// it is the type of the expression. If the "promised type" is itself a Promise-like, the "promised type" is
    /// recursively unwrapped until a non-promise type is found. This is used to reflect the runtime behavior of the
    /// <c>await</c> keyword.
    /// </para>
    /// </remarks>
    internal IType? GetAwaitedType(IType type, Node? errorNode = null, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
    {
        var awaitedType = GetAwaitedTypeNoAlias(type, errorNode, diagnosticMessage, args);
        return awaitedType is not null ? CreateAwaitedTypeIfNeeded(awaitedType) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:43700-43836 — <c>getAwaitedTypeNoAlias</c>.</summary>
    internal IType? GetAwaitedTypeNoAlias(IType type, Node? errorNode = null, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
    {
        if (IsTypeAny(type))
        {
            return type;
        }

        // If this is already an `Awaited<T>`, just return it. This avoids `Awaited<Awaited<T>>` in higher-order
        if (IsAwaitedTypeInstantiation(type))
        {
            return type;
        }

        // If we've already cached an awaited type, return a possible `Awaited<T>` for it.
        var typeAsAwaitable = (UnionOrIntersectionType)type;
        if (typeAsAwaitable.AwaitedTypeOfType is not null)
        {
            return typeAsAwaitable.AwaitedTypeOfType;
        }

        // For a union, get a union of the awaited types of each constituent.
        if ((type.Flags & TypeFlags.Union) != 0)
        {
            if (_awaitedTypeStack.LastIndexOf(type.Id) >= 0)
            {
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Type_is_referenced_directly_or_indirectly_in_the_fulfillment_callback_of_its_own_then_method);
                }
                return null;
            }

            // PORT-NOTE: the reference passes `getAwaitedTypeNoAlias` itself (or a closure over it) as the mapper; its
            // `mapType` has an overload whose mapper may answer `undefined` and drops such constituents
            // (checker.ts:28569), which is the overload this call needs.
            Func<IType, IType?> mapper = errorNode is not null
                ? constituentType => GetAwaitedTypeNoAlias(constituentType, errorNode, diagnosticMessage, args)
                : constituentType => GetAwaitedTypeNoAlias(constituentType);

            _awaitedTypeStack.Add(type.Id);
            var mapped = MapType(type, mapper);
            _awaitedTypeStack.RemoveAt(_awaitedTypeStack.Count - 1);

            return typeAsAwaitable.AwaitedTypeOfType = mapped;
        }

        // If `type` is generic and should be wrapped in `Awaited<T>`, return it.
        if (IsAwaitedTypeNeeded(type))
        {
            return typeAsAwaitable.AwaitedTypeOfType = type;
        }

        var thisTypeForErrorOut = new StrongBox<IType?>(null);
        var promisedType = GetPromisedTypeOfPromise(type, /*errorNode*/ null, thisTypeForErrorOut);
        if (promisedType is not null)
        {
            if (type.Id == promisedType.Id || _awaitedTypeStack.LastIndexOf(promisedType.Id) >= 0)
            {
                // Verify that we don't have a bad actor in the form of a promise whose
                // promised type is the same as the promise type, or a mutually recursive
                // promise. If so, we return undefined as we cannot guess the shape. If this
                // were the actual case in the JavaScript, this Promise would never resolve.
                //
                // An example of a bad actor with a singly-recursive promise type might
                // be:
                //
                //  interface BadPromise {
                //      then(
                //          onfulfilled: (value: BadPromise) => any,
                //          onrejected: (error: any) => any): BadPromise;
                //  }
                //
                // The above interface will pass the PromiseLike check, and return a
                // promised type of `BadPromise`. Since this is a self reference, we
                // don't want to keep recursing ad infinitum.
                //
                // An example of a bad actor in the form of a mutually-recursive
                // promise type might be:
                //
                //  interface BadPromiseA {
                //      then(
                //          onfulfilled: (value: BadPromiseB) => any,
                //          onrejected: (error: any) => any): BadPromiseB;
                //  }
                //
                //  interface BadPromiseB {
                //      then(
                //          onfulfilled: (value: BadPromiseA) => any,
                //          onrejected: (error: any) => any): BadPromiseA;
                //  }
                //
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Type_is_referenced_directly_or_indirectly_in_the_fulfillment_callback_of_its_own_then_method);
                }
                return null;
            }

            // Keep track of the type we're about to unwrap to avoid bad recursive promise types.
            // See the comments above for more information.
            _awaitedTypeStack.Add(type.Id);
            var awaitedType = GetAwaitedTypeNoAlias(promisedType, errorNode, diagnosticMessage, args);
            _awaitedTypeStack.RemoveAt(_awaitedTypeStack.Count - 1);

            if (awaitedType is null)
            {
                return null;
            }

            return typeAsAwaitable.AwaitedTypeOfType = awaitedType;
        }

        // The type was not a promise, so it could not be unwrapped any further.
        // As long as the type does not have a callable "then" property, it is
        // safe to return the type; otherwise, an error is reported and we return
        // undefined.
        //
        // An example of a non-promise "thenable" might be:
        //
        //  await { then(): void {} }
        //
        // The "thenable" does not match the minimal definition for a promise. When
        // a Promise/A+-compatible or ES6 promise tries to adopt this value, the promise
        // will never settle. We treat this as an error to help flag an early indicator
        // of a runtime problem. If the user wants to return this value from an async
        // function, they would need to wrap it in some other value. If they want it to
        // be treated as a promise, they can cast to <any>.
        if (IsThenableType(type))
        {
            if (errorNode is not null)
            {
                Debug.AssertIsDefined(diagnosticMessage);
                IDiagnosticMessageChain? chain = null;
                if (thisTypeForErrorOut.Value is not null)
                {
                    chain = Utilities.ChainDiagnosticMessages(chain, Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1, TypeToString(type), TypeToString(thisTypeForErrorOut.Value));
                }
                chain = Utilities.ChainDiagnosticMessages(chain, diagnosticMessage, args);
                _diagnostics.Add(Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(errorNode)!, errorNode, chain));
            }
            return null;
        }

        return typeAsAwaitable.AwaitedTypeOfType = type;
    }

    /// <summary>
    /// Checks the return type of an async function to ensure it is a compatible Promise implementation.
    /// </summary>
    /// <remarks>
    /// <para>ported from: src/compiler/checker.ts:43838-43935 — <c>checkAsyncFunctionReturnType</c>.</para>
    /// <para>
    /// This checks that an async function has a valid Promise-compatible return type: an async function has a valid
    /// Promise-compatible return type if the resolved value of the return type has a construct signature that takes in
    /// an <c>initializer</c> function that in turn supplies a <c>resolve</c> function as one of its arguments and
    /// results in an object with a callable <c>then</c> signature.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's nested <c>reportErrorForInvalidReturnType</c> reuses the outer parameter names
    /// (<c>returnTypeNode</c>, <c>returnTypeErrorLocation</c>); C# forbids a nested declaration from reusing a name from
    /// an enclosing scope, so its parameters carry a <c>declaration</c> prefix and its body is otherwise the
    /// reference's.
    /// </para>
    /// </remarks>
    internal void CheckAsyncFunctionReturnType(Node node, ITypeNode returnTypeNode, ITypeNode returnTypeErrorLocation)
    {
        // As part of our emit for an async function, we will need to emit the entity name of
        // the return type annotation as an expression. To meet the necessary runtime semantics
        // for __awaiter, we must also check that the type of the declaration (e.g. the static
        // side or "constructor" of the promise type) is compatible `PromiseConstructorLike`.
        //
        // An example might be (from lib.es6.d.ts):
        //
        //  interface Promise<T> { ... }
        //  interface PromiseConstructor {
        //      new <T>(...): Promise<T>;
        //  }
        //  declare var Promise: PromiseConstructor;
        //
        // When an async function declares a return type annotation of `Promise<T>`, we
        // need to get the type of the `Promise` variable declaration above, which would
        // be `PromiseConstructor`.
        //
        // The same case applies to a class:
        //
        //  declare class Promise<T> {
        //      constructor(...);
        //      then<U>(...): Promise<U>;
        //  }
        //
        var returnType = GetTypeFromTypeNode(returnTypeNode);
        if (_languageVersion >= ScriptTarget.ES2015)
        {
            if (IsErrorType(returnType))
            {
                return;
            }
            var globalPromiseType = GetGlobalPromiseType(/*reportErrors*/ true);
            if (!ReferenceEquals(globalPromiseType, _emptyGenericType) && !IsReferenceToType(returnType, globalPromiseType))
            {
                // The promise type was not a valid type reference to the global promise type, so we
                // report an error and return the unknown type.
                ReportErrorForInvalidReturnType(Diagnostics.The_return_type_of_an_async_function_or_method_must_be_the_global_Promise_T_type_Did_you_mean_to_write_Promise_0, returnTypeNode, returnTypeErrorLocation, TypeToString(GetAwaitedTypeNoAlias(returnType) ?? _voidType));
                return;
            }
        }
        else
        {
            // Always mark the type node as referenced if it points to a value
            MarkLinkedReferences(node, ReferenceHint.AsyncFunction);
            if (IsErrorType(returnType))
            {
                return;
            }

            var promiseConstructorName = Utilities.GetEntityNameFromTypeNode(returnTypeNode);
            if (promiseConstructorName is null)
            {
                ReportErrorForInvalidReturnType(Diagnostics.Type_0_is_not_a_valid_async_function_return_type_in_ES5_because_it_does_not_refer_to_a_Promise_compatible_constructor_value, returnTypeNode, returnTypeErrorLocation, TypeToString(returnType));
                return;
            }

            var promiseConstructorSymbol = ResolveEntityName(promiseConstructorName, SymbolFlags.Value, /*ignoreErrors*/ true);
            var promiseConstructorType = promiseConstructorSymbol is not null ? GetTypeOfSymbol(promiseConstructorSymbol) : _errorType;
            if (IsErrorType(promiseConstructorType))
            {
                if (promiseConstructorName.Kind == SyntaxKind.Identifier && ((Identifier)promiseConstructorName).EscapedText == "Promise" && ReferenceEquals(GetTargetType(returnType), GetGlobalPromiseType(/*reportErrors*/ false)))
                {
                    Error((Node)returnTypeErrorLocation, Diagnostics.An_async_function_or_method_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option);
                }
                else
                {
                    ReportErrorForInvalidReturnType(Diagnostics.Type_0_is_not_a_valid_async_function_return_type_in_ES5_because_it_does_not_refer_to_a_Promise_compatible_constructor_value, returnTypeNode, returnTypeErrorLocation, Utilities.EntityNameToString(promiseConstructorName));
                }
                return;
            }

            var globalPromiseConstructorLikeType = GetGlobalPromiseConstructorLikeType(/*reportErrors*/ true);
            if (ReferenceEquals(globalPromiseConstructorLikeType, _emptyObjectType))
            {
                // If we couldn't resolve the global PromiseConstructorLike type we cannot verify
                // compatibility with __awaiter.
                ReportErrorForInvalidReturnType(Diagnostics.Type_0_is_not_a_valid_async_function_return_type_in_ES5_because_it_does_not_refer_to_a_Promise_compatible_constructor_value, returnTypeNode, returnTypeErrorLocation, Utilities.EntityNameToString(promiseConstructorName));
                return;
            }

            var headMessage = Diagnostics.Type_0_is_not_a_valid_async_function_return_type_in_ES5_because_it_does_not_refer_to_a_Promise_compatible_constructor_value;
            Func<IDiagnosticMessageChain?> errorInfo = () => !ReferenceEquals(returnTypeNode, returnTypeErrorLocation)
                ? Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.The_return_type_of_an_async_function_or_method_must_be_the_global_Promise_T_type)
                : null;
            if (!CheckTypeAssignableTo(promiseConstructorType, globalPromiseConstructorLikeType, (Node)returnTypeErrorLocation, headMessage, errorInfo))
            {
                return;
            }

            // Verify there is no local declaration that could collide with the promise constructor.
            var rootName = Utilities.GetFirstIdentifier(promiseConstructorName);
            var collidingSymbol = GetSymbol(((ILocalsContainer)node).Locals!, rootName.EscapedText!, SymbolFlags.Value);
            if (collidingSymbol is not null)
            {
                Error((Node?)collidingSymbol.ValueDeclaration, Diagnostics.Duplicate_identifier_0_Compiler_uses_declaration_1_to_support_async_functions, Utilities.IdText(rootName), Utilities.EntityNameToString(promiseConstructorName));
                return;
            }
        }

        CheckAwaitedType(returnType, /*withAlias*/ false, node, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);

        void ReportErrorForInvalidReturnType(DiagnosticMessage message, ITypeNode declarationReturnTypeNode, ITypeNode declarationReturnTypeErrorLocation, string typeName)
        {
            if (ReferenceEquals(declarationReturnTypeNode, declarationReturnTypeErrorLocation))
            {
                Error((Node)declarationReturnTypeErrorLocation, message, typeName);
            }
            else
            {
                var diagnostic = Error((Node)declarationReturnTypeErrorLocation, Diagnostics.The_return_type_of_an_async_function_or_method_must_be_the_global_Promise_T_type);
                Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode((Node)declarationReturnTypeNode, message, typeName));
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:43937-44008 — <c>checkGrammarDecorator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's loop narrows <c>node</c> with the <c>is*</c> tests and then reads <c>node.expression</c>
    /// / <c>node.questionDotToken</c>; the port's tests answer <c>bool</c> (C# cannot narrow through a predicate), so
    /// each arm casts to the class the test just established, and the two tests of the
    /// <c>ExpressionWithTypeArguments | NonNullExpression</c> arm become two <c>if</c>s with the same bodies and the
    /// same <c>continue</c>.
    /// </remarks>
    internal bool CheckGrammarDecorator(Decorator decorator)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(decorator)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            Expression node = decorator.Expression!;

            // DecoratorParenthesizedExpression :
            //   `(` Expression `)`

            if (NodeTests.IsParenthesizedExpression(node))
            {
                return false;
            }

            var canHaveCallExpression = true;
            Node? errorNode = null;
            while (true)
            {
                // Allow TS syntax such as non-null assertions and instantiation expressions
                if (NodeTests.IsExpressionWithTypeArguments(node))
                {
                    node = ((ExpressionWithTypeArguments)node).Expression!;
                    continue;
                }
                if (NodeTests.IsNonNullExpression(node))
                {
                    node = ((NonNullExpression)node).Expression!;
                    continue;
                }

                // DecoratorCallExpression :
                //   DecoratorMemberExpression Arguments

                if (NodeTests.IsCallExpression(node))
                {
                    if (!canHaveCallExpression)
                    {
                        errorNode = node;
                    }
                    if (((CallExpression)node).QuestionDotToken is not null)
                    {
                        // Even if we already have an error node, error at the `?.` token since it appears earlier.
                        errorNode = ((CallExpression)node).QuestionDotToken;
                    }
                    node = ((CallExpression)node).Expression!;
                    canHaveCallExpression = false;
                    continue;
                }

                // DecoratorMemberExpression :
                //   IdentifierReference
                //   DecoratorMemberExpression `.` IdentifierName
                //   DecoratorMemberExpression `.` PrivateIdentifier

                if (NodeTests.IsPropertyAccessExpression(node))
                {
                    if (((PropertyAccessExpression)node).QuestionDotToken is not null)
                    {
                        // Even if we already have an error node, error at the `?.` token since it appears earlier.
                        errorNode = ((PropertyAccessExpression)node).QuestionDotToken;
                    }
                    node = ((PropertyAccessExpression)node).Expression!;
                    canHaveCallExpression = false;
                    continue;
                }

                if (!NodeTests.IsIdentifier(node))
                {
                    // Even if we already have an error node, error at this node since it appears earlier.
                    errorNode = node;
                }

                break;
            }

            if (errorNode is not null)
            {
                Utilities.AddRelatedInfo(
                    Error(decorator.Expression, Diagnostics.Expression_must_be_enclosed_in_parentheses_to_be_used_as_a_decorator),
                    Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.Invalid_syntax_in_decorator));
                return true;
            }
        }

        return false;
    }

    /// <summary>Check a decorator.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:44010-44057 — <c>checkDecorator</c>.</remarks>
    internal void CheckDecorator(Decorator node)
    {
        CheckGrammarDecorator(node);

        var signature = GetResolvedSignature(node);
        CheckDeprecatedSignature(signature, node);
        var returnType = GetReturnTypeOfSignature(signature);
        if ((returnType.Flags & TypeFlags.Any) != 0)
        {
            return;
        }

        // if we fail to get a signature and return type here, we will have already reported a grammar error in `checkDecorators`.
        var decoratorSignature = GetDecoratorCallSignature(node);
        if (decoratorSignature?.ResolvedReturnType is null) return;

        DiagnosticMessage headMessage;
        var expectedReturnType = decoratorSignature.ResolvedReturnType;
        switch (node.Parent!.Kind)
        {
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                headMessage = Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1;
                break;

            case SyntaxKind.PropertyDeclaration:
                if (!_legacyDecorators)
                {
                    headMessage = Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1;
                    break;
                }
                // falls through
                goto case SyntaxKind.Parameter;

            case SyntaxKind.Parameter:
                headMessage = Diagnostics.Decorator_function_return_type_is_0_but_is_expected_to_be_void_or_any;
                break;

            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                headMessage = Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1;
                break;

            default:
                Debug.FailBadSyntaxKind(node.Parent!);
                return;
        }

        CheckTypeAssignableTo(returnType, expectedReturnType, node.Expression, headMessage);
    }

    /// <summary>Creates a synthetic `Signature` corresponding to a call signature.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:44059-44072 — <c>createCallSignature</c>.</remarks>
    internal Signature CreateCallSignature(List<ITypeParameter>? typeParameters, ISymbol? thisParameter, List<ISymbol> parameters, IType returnType, ITypePredicateBase? typePredicate = null, int? minArgumentCount = null, SignatureFlags flags = SignatureFlags.None)
    {
        var decl = FactoryUtilities.ParseNodeFactory.CreateFunctionTypeNode(/*typeParameters*/ null, [], FactoryUtilities.ParseNodeFactory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
        return CreateSignature(decl, typeParameters, thisParameter, parameters, returnType, typePredicate, minArgumentCount ?? parameters.Count, flags);
    }

    /// <summary>Creates a synthetic `FunctionType`.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:44075-44085 — <c>createFunctionType</c>.</remarks>
    internal IType CreateFunctionType(List<ITypeParameter>? typeParameters, ISymbol? thisParameter, List<ISymbol> parameters, IType returnType, ITypePredicateBase? typePredicate = null, int? minArgumentCount = null, SignatureFlags? flags = null)
    {
        var signature = CreateCallSignature(typeParameters, thisParameter, parameters, returnType, typePredicate, minArgumentCount, flags ?? SignatureFlags.None);
        return GetOrCreateTypeFromSignature(signature);
    }

    /// <summary>ported from: src/compiler/checker.ts:44088-44090 — <c>createGetterFunctionType</c>.</summary>
    internal IType CreateGetterFunctionType(IType type)
        => CreateFunctionType(/*typeParameters*/ null, /*thisParameter*/ null, [], type);

    /// <summary>ported from: src/compiler/checker.ts:44092-44095 — <c>createSetterFunctionType</c>.</summary>
    internal IType CreateSetterFunctionType(IType type)
    {
        var valueParam = CreateParameter("value", type);
        return CreateFunctionType(/*typeParameters*/ null, /*thisParameter*/ null, [valueParam], _voidType);
    }

    /// <summary>ported from: src/compiler/checker.ts:44097-44115 — <c>getEntityNameForDecoratorMetadata</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is the <c>EntityName</c> union, which the port has no C# type for; it is the port's
    /// <see cref="Node"/> (the two arms are <see cref="Identifier"/> and <see cref="QualifiedName"/>), and each
    /// <c>(node as …)</c> cast of the reference is stated against the class its own kind test selected.
    /// </remarks>
    internal Node? GetEntityNameForDecoratorMetadata(Node? node)
    {
        if (node is not null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.IntersectionType:
                case SyntaxKind.UnionType:
                    return GetEntityNameForDecoratorMetadataFromTypeList(node.Kind == SyntaxKind.UnionType
                        ? ((UnionTypeNode)node).Types!
                        : ((IntersectionTypeNode)node).Types!);

                case SyntaxKind.ConditionalType:
                    var conditionalType = (ConditionalTypeNode)node;
                    return GetEntityNameForDecoratorMetadataFromTypeList([conditionalType.TrueType!, conditionalType.FalseType!]);

                case SyntaxKind.ParenthesizedType:
                case SyntaxKind.NamedTupleMember:
                    {
                        var nestedType = node.Kind == SyntaxKind.ParenthesizedType ? (Node?)((ParenthesizedTypeNode)node).Type : (Node?)((NamedTupleMember)node).Type;
                        return GetEntityNameForDecoratorMetadata(nestedType);
                    }

                case SyntaxKind.TypeReference:
                    return ((TypeReferenceNode)node).TypeName;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:44117-44155 — <c>getEntityNameForDecoratorMetadataFromTypeList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reassigns its <c>for…of</c> binding while skipping parentheses; C# forbids assigning the
    /// iteration variable, so each iteration copies it into <c>typeNode</c> and walks that copy — the same reads, in the
    /// same order.
    /// </remarks>
    internal Node? GetEntityNameForDecoratorMetadataFromTypeList(IList<ITypeNode> types)
    {
        Node? commonEntityName = null;
        foreach (var type in types)
        {
            var typeNode = type;
            while (NodeTests.IsParenthesizedTypeNode((Node)typeNode) || NodeTests.IsNamedTupleMember((Node)typeNode))
            {
                typeNode = typeNode.Kind == SyntaxKind.ParenthesizedType ? ((ParenthesizedTypeNode)typeNode).Type! : ((NamedTupleMember)typeNode).Type!; // Skip parens if need be
            }
            if (typeNode.Kind == SyntaxKind.NeverKeyword)
            {
                continue; // Always elide `never` from the union/intersection if possible
            }
            if (!_strictNullChecks && (typeNode.Kind == SyntaxKind.LiteralType && ((LiteralTypeNode)typeNode).Literal!.Kind == SyntaxKind.NullKeyword || typeNode.Kind == SyntaxKind.UndefinedKeyword))
            {
                continue; // Elide null and undefined from unions for metadata, just like what we did prior to the implementation of strict null checks
            }
            var individualEntityName = GetEntityNameForDecoratorMetadata((Node)typeNode);
            if (individualEntityName is null)
            {
                // Individual is something like string number
                // So it would be serialized to either that type or object
                // Safe to return here
                return null;
            }

            if (commonEntityName is not null)
            {
                // Note this is in sync with the transformation that happens for type node.
                // Keep this in sync with serializeUnionOrIntersectionType
                // Verify if they refer to same entity and is identifier
                // return undefined if they dont match because we would emit object
                if (
                    !NodeTests.IsIdentifier(commonEntityName)
                    || !NodeTests.IsIdentifier(individualEntityName)
                    || ((Identifier)commonEntityName).EscapedText != ((Identifier)individualEntityName).EscapedText
                )
                {
                    return null;
                }
            }
            else
            {
                commonEntityName = individualEntityName;
            }
        }
        return commonEntityName;
    }

    /// <summary>ported from: src/compiler/checker.ts:44157-44160 — <c>getParameterTypeNodeForDecoratorCheck</c>.</summary>
    internal ITypeNode? GetParameterTypeNodeForDecoratorCheck(ParameterDeclaration node)
    {
        var typeNode = Utilities.GetEffectiveTypeAnnotationNode(node);
        return Utilities.IsRestParameter(node) ? Utilities.GetRestParameterElementType(typeNode) : typeNode;
    }

    /// <summary>Check the decorators of a node.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's guard reads <c>node.modifiers</c> and <c>node.name</c> off the <c>HasDecorators</c>
    /// union; C# has no interface for that alias, so the modifier list is read through <c>Utilities.ModifiersOf</c> —
    /// the port's own accessor for TS's <c>HasModifiers</c> kinds (<c>Utilities/Utilities.Binder.cs</c>, widened to
    /// <c>internal</c> for this reader) — and the name through <see cref="INamedDeclaration"/>. <c>hasDecorators(node)</c> (utilities.ts:7236-7238) is
    /// stated as its own body, <c>hasSyntacticModifier(node, ModifierFlags.Decorator)</c>, and the reference's
    /// <c>find(modifiers, isDecorator)</c> loop is the first-match scan below (the same predicate over the same order).
    /// </remarks>
    internal void CheckDecorators(Node node)
    {
        // skip this check for nodes that cannot have decorators. These should have already had an error reported by
        // checkGrammarModifiers.
        var modifiers = Utilities.ModifiersOf(node);
        var nodeName = ((INamedDeclaration)node).Name;
        if (modifiers is null || !Utilities.CanHaveDecorators(node)
            || !Utilities.HasSyntacticModifier(node, ModifierFlags.Decorator)
            || !Utilities.NodeCanBeDecorated(_legacyDecorators, node, node.Parent!, node.Parent!.Parent!))
        {
            return;
        }

        Decorator? firstDecorator = null;
        foreach (var modifier in modifiers!)
        {
            if (NodeTests.IsDecorator(modifier))
            {
                firstDecorator = (Decorator)modifier;
                break;
            }
        }
        if (firstDecorator is null)
        {
            return;
        }

        if (_legacyDecorators)
        {
            CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.Decorate);
            if (node.Kind == SyntaxKind.Parameter)
            {
                CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.Param);
            }
        }
        else if (_languageVersion < ScriptTarget.ESNext) // LanguageFeatureMinimumTarget.ClassAndClassElementDecorators
        {
            CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.ESDecorateAndRunInitializers);
            if (NodeTests.IsClassDeclaration(node))
            {
                if (nodeName is null)
                {
                    CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.SetFunctionName);
                }
                else
                {
                    var member = GetFirstTransformableStaticClassElement((IClassLikeDeclarationBase)node);
                    if (member is not null)
                    {
                        CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.SetFunctionName);
                    }
                }
            }
            else if (!NodeTests.IsClassExpression(node))
            {
                if (nodeName is not null && NodeTests.IsPrivateIdentifier(nodeName) && (NodeTests.IsMethodDeclaration(node) || Utilities.IsAccessor(node) || Utilities.IsAutoAccessorPropertyDeclaration(node)))
                {
                    CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.SetFunctionName);
                }
                if (nodeName is not null && NodeTests.IsComputedPropertyName(nodeName))
                {
                    CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.PropKey);
                }
            }
        }

        MarkLinkedReferences(node, ReferenceHint.Decorator);

        foreach (var modifier in modifiers!)
        {
            if (NodeTests.IsDecorator(modifier))
            {
                CheckDecorator((Decorator)modifier);
            }
        }
    }
    // ---------------------------------------------------------------------------------------------
    // Function declarations and the JSDoc tags (checker.ts:44213-44345)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:44213-44221 — <c>checkFunctionDeclaration</c>.</summary>
    internal void CheckFunctionDeclaration(FunctionDeclaration node)
    {
        AddLazyDiagnostic(CheckFunctionDeclarationDiagnostics);

        void CheckFunctionDeclarationDiagnostics()
        {
            CheckFunctionOrMethodDeclaration(node);
            CheckGrammarForGenerator(node);
            CheckCollisionsForDeclarationName(node, (Identifier?)((INamedDeclaration)node).Name);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44223-44234 — <c>checkJSDocTypeAliasTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>JSDocTypedefTag | JSDocCallbackTag</c> union, whose two arms declare
    /// <c>typeExpression</c> with different types and <c>name</c> alike; the port names both classes and takes each one's
    /// own slots (the treatment <c>Checker.Queries.cs</c> gives the <c>JSDocTypeAlias</c> union).
    /// </remarks>
    internal void CheckJSDocTypeAliasTag(Node node)
    {
        Node? typeExpression;
        Identifier? name;
        if (node is JSDocTypedefTag typedefTag)
        {
            typeExpression = typedefTag.TypeExpression;
            name = typedefTag.Name;
        }
        else
        {
            var callbackTag = (JSDocCallbackTag)node;
            typeExpression = callbackTag.TypeExpression;
            name = callbackTag.Name;
        }

        if (typeExpression is null)
        {
            // If the node had `@property` tags, `typeExpression` would have been set to the first property tag.
            Error(name, Diagnostics.JSDoc_typedef_tag_should_either_have_a_type_annotation_or_be_followed_by_property_or_member_tags);
        }

        if (name is not null)
        {
            CheckTypeNameIsReserved(name, Diagnostics.Type_alias_name_cannot_be_0);
        }
        CheckSourceElement(typeExpression);
        CheckTypeParameters(Utilities.GetEffectiveTypeParameterDeclarations(node));
    }

    /// <summary>ported from: src/compiler/checker.ts:44236-44241 — <c>checkJSDocTemplateTag</c>.</summary>
    internal void CheckJSDocTemplateTag(JSDocTemplateTag node)
    {
        CheckSourceElement(node.Constraint);
        foreach (var typeParameter in node.TypeParameters!)
        {
            CheckSourceElement(typeParameter);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44243-44245 — <c>checkJSDocTypeTag</c>.</summary>
    internal void CheckJSDocTypeTag(JSDocTypeTag node) => CheckSourceElement(node.TypeExpression);

    /// <summary>ported from: src/compiler/checker.ts:44247-44259 — <c>checkJSDocSatisfiesTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks for the host's tags with <c>getAllJSDocTags(host, isJSDocSatisfiesTag)</c>
    /// (utilitiesPublic.ts:1284-1286), which is <c>getJSDocTags(node).filter(predicate)</c>; the port has no
    /// <c>GetAllJSDocTags</c> member (the landed <c>Utilities.Checker.cs</c> records the same substitution), so the
    /// reference's own filter is stated with <c>Core.Filter</c>.
    /// </remarks>
    internal void CheckJSDocSatisfiesTag(JSDocSatisfiesTag node)
    {
        CheckSourceElement(node.TypeExpression);
        var host = Utilities.GetEffectiveJSDocHost(node);
        if (host is not null)
        {
            var tags = Core.Filter(Utilities.GetJSDocTags(host), NodeTests.IsJSDocSatisfiesTag)!;
            if (tags.Count > 1)
            {
                for (var i = 1; i < tags.Count; i++)
                {
                    var tagName = tags[i].TagName!;
                    Error(tagName, Diagnostics._0_tag_already_specified, Utilities.IdText(tagName));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44261-44265 — <c>checkJSDocLinkLikeTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>JSDocLink | JSDocLinkCode | JSDocLinkPlain</c> union; the three classes
    /// declare no shared base for <c>name</c>, so it is read through the switch below (the <c>_</c> arm is the third
    /// class the union leaves).
    /// </remarks>
    internal void CheckJSDocLinkLikeTag(Node node)
    {
        var nodeName = node switch
        {
            JSDocLink link => link.Name,
            JSDocLinkCode linkCode => linkCode.Name,
            _ => ((JSDocLinkPlain)node).Name,
        };
        if (nodeName is not null)
        {
            ResolveJSDocMemberName(nodeName, /*ignoreErrors*/ true);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44267-44269 — <c>checkJSDocParameterTag</c>.</summary>
    internal void CheckJSDocParameterTag(JSDocParameterTag node) => CheckSourceElement(node.TypeExpression);

    /// <summary>ported from: src/compiler/checker.ts:44271-44273 — <c>checkJSDocPropertyTag</c>.</summary>
    internal void CheckJSDocPropertyTag(JSDocPropertyTag node) => CheckSourceElement(node.TypeExpression);

    /// <summary>ported from: src/compiler/checker.ts:44275-44284 — <c>checkJSDocFunctionType</c>.</summary>
    internal void CheckJSDocFunctionType(JSDocFunctionType node)
    {
        AddLazyDiagnostic(CheckJSDocFunctionTypeImplicitAny);
        CheckSignatureDeclaration(node);

        void CheckJSDocFunctionTypeImplicitAny()
        {
            if (node.Type is null && !Utilities.IsJSDocConstructSignature(node))
            {
                ReportImplicitAny(node, _anyType);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44286-44291 — <c>checkJSDocThisTag</c>.</summary>
    internal void CheckJSDocThisTag(JSDocThisTag node)
    {
        var host = Utilities.GetEffectiveJSDocHost(node);
        if (host is not null && NodeTests.IsArrowFunction(host))
        {
            Error(node.TagName, Diagnostics.An_arrow_function_cannot_have_a_this_parameter);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44293-44295 — <c>checkJSDocImportTag</c>.</summary>
    internal void CheckJSDocImportTag(JSDocImportTag node) => CheckImportAttributes(node);

    /// <summary>ported from: src/compiler/checker.ts:44297-44302 — <c>checkJSDocImplementsTag</c>.</summary>
    internal void CheckJSDocImplementsTag(JSDocImplementsTag node)
    {
        var classLike = Utilities.GetEffectiveJSDocHost(node);
        if (classLike is null || !NodeTests.IsClassDeclaration(classLike) && !NodeTests.IsClassExpression(classLike))
        {
            Error(classLike, Diagnostics.JSDoc_0_is_not_attached_to_a_class, Utilities.IdText(node.TagName!));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44304-44325 — <c>checkJSDocAugmentsTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference compares the two names by <c>escapedText</c>; that read is
    /// <c>Utilities.GetEscapedTextOfIdentifierOrLiteral</c>, the port's helper for it (the same substitution
    /// <c>Checker.Relations.Structured.cs</c> makes).
    /// </remarks>
    internal void CheckJSDocAugmentsTag(JSDocAugmentsTag node)
    {
        var classLike = Utilities.GetEffectiveJSDocHost(node);
        if (classLike is null || !NodeTests.IsClassDeclaration(classLike) && !NodeTests.IsClassExpression(classLike))
        {
            Error(classLike, Diagnostics.JSDoc_0_is_not_attached_to_a_class, Utilities.IdText(node.TagName!));
            return;
        }

        var augmentsTags = Core.Filter(Utilities.GetJSDocTags(classLike), NodeTests.IsJSDocAugmentsTag)!;
        Debug.Assert(augmentsTags.Count > 0);
        if (augmentsTags.Count > 1)
        {
            Error(augmentsTags[1], Diagnostics.Class_declarations_cannot_have_more_than_one_augments_or_extends_tag);
        }

        var name = GetIdentifierFromEntityNameExpression(node.Class!.Expression!);
        var extend = Utilities.GetClassExtendsHeritageElement(classLike);
        if (extend is not null)
        {
            var className = GetIdentifierFromEntityNameExpression(extend.Expression!);
            if (className is not null && Utilities.GetEscapedTextOfIdentifierOrLiteral(name!) != Utilities.GetEscapedTextOfIdentifierOrLiteral(className))
            {
                Error(name, Diagnostics.JSDoc_0_1_does_not_match_the_extends_2_clause, Utilities.IdText(node.TagName!), Utilities.IdText(name!), Utilities.IdText(className));
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44327-44332 — <c>checkJSDocAccessibilityModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>JSDocPublicTag | JSDocProtectedTag | JSDocPrivateTag</c> union, whose three
    /// arms carry no slot this member reads, so the port's parameter is <see cref="Node"/>.
    /// </remarks>
    internal void CheckJSDocAccessibilityModifiers(Node node)
    {
        var host = Utilities.GetJSDocHost(node);
        if (host is not null && Utilities.IsPrivateIdentifierClassElementDeclaration(host))
        {
            Error(node, Diagnostics.An_accessibility_modifier_cannot_be_used_with_a_private_identifier);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44334-44345 — <c>getIdentifierFromEntityNameExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads whose answers are the <c>Identifier | PrivateIdentifier</c> union; the port
    /// has no C# type for that union, so the single member answers <see cref="Node"/> — one of the two classes for the
    /// two arms and <c>null</c> for the reference's <c>undefined</c>.
    /// </remarks>
    internal static Node? GetIdentifierFromEntityNameExpression(Node node) => node.Kind switch
    {
        SyntaxKind.Identifier => node,
        SyntaxKind.PropertyAccessExpression => ((PropertyAccessExpression)node).Name,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:44347-44417 — <c>checkFunctionOrMethodDeclaration</c>.</summary>
    internal void CheckFunctionOrMethodDeclaration(Node node)
    {
        CheckDecorators(node);
        CheckSignatureDeclaration(node);
        var functionFlags = Utilities.GetFunctionFlags(node);

        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        var nodeName = ((INamedDeclaration)node).Name;
        if (nodeName is not null && nodeName.Kind == SyntaxKind.ComputedPropertyName)
        {
            // This check will account for methods in class/interface declarations,
            // as well as accessors in classes/object literals
            CheckComputedPropertyName((ComputedPropertyName)nodeName);
        }

        if (HasBindableName((IDeclaration)node))
        {
            // first we want to check the local symbol that contain this declaration
            // - if node.localSymbol !== undefined - this is current declaration is exported and localSymbol points to the local symbol
            // - if node.localSymbol === undefined - this node is non-exported so we can just pick the result of getSymbolOfNode
            var symbol = GetSymbolOfDeclaration((IDeclaration)node);
            var localSymbol = ((IDeclaration)node).LocalSymbol ?? symbol;

            // Since the javascript won't do semantic analysis like typescript,
            // if the javascript file comes before the typescript file and both contain same name functions,
            // checkFunctionOrConstructorSymbol wouldn't be called if we didnt ignore javascript function.
            var firstDeclaration = Core.Find(localSymbol.Declarations, (IDeclaration declaration, int _) => declaration.Kind == node.Kind && (declaration.Flags & NodeFlags.JavaScriptFile) == 0);

            // Only type check the symbol once
            if (ReferenceEquals(node, firstDeclaration))
            {
                CheckFunctionOrConstructorSymbol(localSymbol);
            }

            if (symbol.Parent is not null)
            {
                // run check on export symbol to check that modifiers agree across all exported declarations
                CheckFunctionOrConstructorSymbol(symbol);
            }
        }

        Node? body = node.Kind == SyntaxKind.MethodSignature ? null : ((IFunctionLikeDeclarationBase)node).Body;
        CheckSourceElement(body);
        CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, GetReturnTypeFromAnnotation(node));

        AddLazyDiagnostic(CheckFunctionOrMethodDeclarationDiagnostics);

        // A js function declaration can have a @type tag instead of a return type node, but that type must have a call signature
        if (Utilities.IsInJSFile(node))
        {
            var typeTag = Utilities.GetJSDocTypeTag(node);
            if (typeTag?.TypeExpression is not null && GetContextualCallSignature(GetTypeFromTypeNode(typeTag.TypeExpression), node) is null)
            {
                Error((Node)typeTag.TypeExpression.Type!, Diagnostics.The_type_of_a_function_declaration_must_match_the_function_s_signature);
            }
        }

        void CheckFunctionOrMethodDeclarationDiagnostics()
        {
            if (Utilities.GetEffectiveReturnTypeNode(node) is null)
            {
                // Report an implicit any error if there is no body, no explicit return type, and node is not a private method
                // in an ambient context
                if (Utilities.NodeIsMissing(body) && !IsPrivateWithinAmbient(node))
                {
                    ReportImplicitAny((IDeclaration)node, _anyType);
                }

                if ((functionFlags & FunctionFlags.Generator) != 0 && Utilities.NodeIsPresent(body))
                {
                    // A generator with a body and no type annotation can still cause errors. It can error if the
                    // yielded values have no common supertype, or it can give an implicit any error if it has no
                    // yielded values. The only way to trigger these errors is to try checking its return type.
                    GetReturnTypeOfSignature(GetSignatureFromDeclaration(node));
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The unused-identifier family (checker.ts:44419-44761)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:44419-44434 — <c>registerForUnusedIdentifiersCheck</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PotentiallyUnusedIdentifier</c> union; the port has no C# type for it and the
    /// member stores it for <c>checkUnusedIdentifiers</c>, which switches on <c>kind</c>, so the parameter is
    /// <see cref="Node"/>. The reference's commented-out <c>addToSeen</c> assertion (the <c>TODO: GH#22580</c> line) is
    /// kept as the reference has it, which is why nothing writes <c>NodeCheckFlags</c> here.
    /// </remarks>
    internal void RegisterForUnusedIdentifiersCheck(Node node)
    {
        AddLazyDiagnostic(RegisterForUnusedIdentifiersCheckDiagnostics);

        void RegisterForUnusedIdentifiersCheckDiagnostics()
        {
            // May be in a call such as getTypeOfNode that happened to call this. But potentiallyUnusedIdentifiers is only defined in the scope of `checkSourceFile`.
            var sourceFile = Utilities.GetSourceFileOfNode(node)!;
            var path = sourceFile.Path!;
            if (!_allPotentiallyUnusedIdentifiers.TryGetValue(path, out var potentiallyUnusedIdentifiers))
            {
                potentiallyUnusedIdentifiers = [];
                _allPotentiallyUnusedIdentifiers[path] = potentiallyUnusedIdentifiers;
            }
            // TODO: GH#22580
            // Debug.assert(addToSeen(seenPotentiallyUnusedIdentifiers, getNodeId(node)), "Adding potentially-unused identifier twice");
            potentiallyUnusedIdentifiers.Add(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44438-44484 — <c>checkUnusedIdentifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PotentiallyUnusedIdentifier</c> union; the port's is the list of the same
    /// nodes (the reader switches on <c>kind</c> and casts where it hands one on, exactly as the reference's narrowing
    /// does).
    /// </remarks>
    internal void CheckUnusedIdentifiers(IList<Node> potentiallyUnusedIdentifiers, AddUnusedDiagnostic addDiagnostic)
    {
        foreach (var node in potentiallyUnusedIdentifiers)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                    CheckUnusedClassMembers(node, addDiagnostic);
                    CheckUnusedTypeParameters(node, addDiagnostic);
                    break;
                case SyntaxKind.SourceFile:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.Block:
                case SyntaxKind.CaseBlock:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                    CheckUnusedLocalsAndParameters(node, addDiagnostic);
                    break;
                case SyntaxKind.Constructor:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    // Only report unused parameters on the implementation, not overloads.
                    if (((IFunctionLikeDeclarationBase)node).Body is not null)
                    {
                        CheckUnusedLocalsAndParameters(node, addDiagnostic);
                    }
                    CheckUnusedTypeParameters(node, addDiagnostic);
                    break;
                case SyntaxKind.MethodSignature:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                    CheckUnusedTypeParameters(node, addDiagnostic);
                    break;
                case SyntaxKind.InferType:
                    CheckUnusedInferTypeParameter((InferTypeNode)node, addDiagnostic);
                    break;
                default:
                    Debug.AssertNever(node, "Node should not have been registered for unused identifiers check");
                    break;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44486-44490 — <c>errorUnusedLocal</c>.</summary>
    internal void ErrorUnusedLocal(IDeclaration declaration, string name, AddUnusedDiagnostic addDiagnostic)
    {
        var node = (Node?)Utilities.GetNameOfDeclaration((Node)declaration) ?? (Node)declaration;
        var message = Utilities.IsTypeDeclaration((Node)declaration)
            ? Diagnostics._0_is_declared_but_never_used
            : Diagnostics._0_is_declared_but_its_value_is_never_read;
        addDiagnostic((Node)declaration, UnusedKind.Local, Utilities.CreateDiagnosticForNode(node, message, name));
    }

    /// <summary>ported from: src/compiler/checker.ts:44492-44494 — <c>isIdentifierThatStartsWithUnderscore</c>.</summary>
    /// <remarks>PORT-NOTE: TS tests the first character's code against <c>CharacterCodes._</c>; the port tests the same first character of the same text.</remarks>
    internal static bool IsIdentifierThatStartsWithUnderscore(Node node)
    {
        if (!NodeTests.IsIdentifier(node))
        {
            return false;
        }
        var text = Utilities.IdText(node);
        return text.Length != 0 && text[0] == '_';
    }

    /// <summary>ported from: src/compiler/checker.ts:44496-44532 — <c>checkUnusedClassMembers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ClassDeclaration | ClassExpression</c>, whose port face
    /// (<see cref="IClassLikeDeclarationBase"/>) declares <c>members</c>, so the slot is read through it; the reference's
    /// <c>member.name</c>/<c>member.symbol</c> reads go through <see cref="INamedDeclaration"/> and
    /// <see cref="IDeclaration"/>.
    /// </remarks>
    internal void CheckUnusedClassMembers(Node node, AddUnusedDiagnostic addDiagnostic)
    {
        var members = ((IClassLikeDeclarationBase)node).Members;
        Debug.Assert(members is not null, "a class-like declaration always has a members list");
        foreach (var member in members!)
        {
            switch (member.Kind)
            {
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    if (member.Kind == SyntaxKind.SetAccessor && (member.Symbol!.Flags & SymbolFlags.GetAccessor) != 0)
                    {
                        // Already would have reported an error on the getter.
                        break;
                    }
                    var symbol = GetSymbolOfDeclaration(member);
                    if (
                        (symbol.IsReferenced ?? SymbolFlags.None) == 0
                        && (Utilities.HasEffectiveModifier((Node)member, ModifierFlags.Private) || Utilities.IsNamedDeclaration((Node)member) && NodeTests.IsPrivateIdentifier(((INamedDeclaration)member).Name!))
                        && (member.Flags & NodeFlags.Ambient) == 0
                    )
                    {
                        addDiagnostic((Node)member, UnusedKind.Local, Utilities.CreateDiagnosticForNode(((INamedDeclaration)member).Name!, Diagnostics._0_is_declared_but_its_value_is_never_read, SymbolToString(symbol)));
                    }
                    break;
                case SyntaxKind.Constructor:
                    foreach (var parameter in ((ConstructorDeclaration)member).Parameters!)
                    {
                        if ((parameter.Symbol!.IsReferenced ?? SymbolFlags.None) == 0 && Utilities.HasSyntacticModifier(parameter, ModifierFlags.Private))
                        {
                            addDiagnostic(parameter, UnusedKind.Local, Utilities.CreateDiagnosticForNode(parameter.Name!, Diagnostics.Property_0_is_declared_but_its_value_is_never_read, Utilities.SymbolName(parameter.Symbol!)));
                        }
                    }
                    break;
                case SyntaxKind.IndexSignature:
                case SyntaxKind.SemicolonClassElement:
                case SyntaxKind.ClassStaticBlockDeclaration:
                    // Can't be private
                    break;
                default:
                    Debug.Fail("Unexpected class member");
                    break;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44534-44539 — <c>checkUnusedInferTypeParameter</c>.</summary>
    internal void CheckUnusedInferTypeParameter(InferTypeNode node, AddUnusedDiagnostic addDiagnostic)
    {
        var typeParameter = node.TypeParameter!;
        if (IsTypeParameterUnused(typeParameter))
        {
            addDiagnostic(node, UnusedKind.Parameter, Utilities.CreateDiagnosticForNode(node, Diagnostics._0_is_declared_but_its_value_is_never_read, Utilities.IdText(typeParameter.Name!)));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44541-44576 — <c>checkUnusedTypeParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ClassLikeDeclaration | SignatureDeclaration | InterfaceDeclaration |
    /// TypeAliasDeclaration</c> union, whose port face is <see cref="Node"/>; the reference's
    /// <c>parent.typeParameters</c> read is <c>Utilities.TypeParametersOf</c>, and the reference's
    /// <c>DiagnosticAndArguments</c> pair is stated as the two <c>CreateFileDiagnostic</c> calls it stands for.
    /// </remarks>
    internal void CheckUnusedTypeParameters(Node node, AddUnusedDiagnostic addDiagnostic)
    {
        // Only report errors on the last declaration for the type parameter container;
        // this ensures that all uses have been accounted for.
        var declarations = GetSymbolOfDeclaration((IDeclaration)node).Declarations;
        if (declarations is null || !ReferenceEquals(Core.Last(declarations), node)) return;

        var typeParameters = Utilities.GetEffectiveTypeParameterDeclarations(node);
        var seenParentsWithEveryUnused = new HashSet<Node>();

        foreach (var typeParameter in typeParameters)
        {
            if (!IsTypeParameterUnused(typeParameter)) continue;

            var name = Utilities.IdText(typeParameter.Name!);
            var parent = typeParameter.Parent!;
            var parentTypeParameters = Utilities.TypeParametersOf(parent);
            if (parent.Kind != SyntaxKind.InferType && parentTypeParameters is not null && Core.Every(parentTypeParameters, (TypeParameterDeclaration tp, int _) => IsTypeParameterUnused(tp)))
            {
                if (Core.TryAddToSet(seenParentsWithEveryUnused, parent))
                {
                    var sourceFile = Utilities.GetSourceFileOfNode(parent)!;
                    var range = NodeTests.IsJSDocTemplateTag(parent)
                        // Whole @template tag
                        ? Utilities.RangeOfNode(parent)
                        // Include the `<>` in the error message
                        : Utilities.RangeOfTypeParameters(sourceFile, parentTypeParameters);
                    var only = parentTypeParameters.Count == 1;
                    // TODO: following line is possible reason for bug #41974, unusedTypeParameters_TemplateTag
                    addDiagnostic(typeParameter, UnusedKind.Parameter, only
                        ? Utilities.CreateFileDiagnostic(sourceFile, range.Pos, range.End - range.Pos, Diagnostics._0_is_declared_but_its_value_is_never_read, name)
                        : Utilities.CreateFileDiagnostic(sourceFile, range.Pos, range.End - range.Pos, Diagnostics.All_type_parameters_are_unused));
                }
            }
            else
            {
                // TODO: following line is possible reason for bug #41974, unusedTypeParameters_TemplateTag
                addDiagnostic(typeParameter, UnusedKind.Parameter, Utilities.CreateDiagnosticForNode(typeParameter, Diagnostics._0_is_declared_but_its_value_is_never_read, name));
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44577-44579 — <c>isTypeParameterUnused</c>.</summary>
    internal bool IsTypeParameterUnused(TypeParameterDeclaration typeParameter)
        => ((GetMergedSymbol(typeParameter.Symbol!)!.IsReferenced ?? SymbolFlags.None) & SymbolFlags.TypeParameter) == 0 && !IsIdentifierThatStartsWithUnderscore(typeParameter.Name!);

    // The `parent.typeParameters` read of `checkUnusedTypeParameters` (checker.ts:44577) is `Utilities.TypeParametersOf`
    // — the `DeclarationWithTypeParameterChildren` slot (types.ts:3536-3541), whose single home that member is
    // (docs/architecture.md §15.15's "one name, one owner, one home"). The `InferTypeNode` that can also be a type
    // parameter's parent is not a member of the union, and `checkUnusedTypeParameters`'s own kind test excludes it.

    /// <summary>ported from: src/compiler/checker.ts:44581-44590 — <c>addToGroup</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Map&lt;string, [K, V[]]&gt;</c> is the port's
    /// <c>OrderedDictionary&lt;string, (K Key, List&lt;V&gt; Values)&gt;</c> (the tuple keeps the key beside the values,
    /// exactly as the reference does, and the ordered map keeps the reference's iteration order), and TS's
    /// <c>getKey: (key: K) =&gt; number | string</c> with its <c>String(…)</c> conversion is stated as
    /// <c>Func&lt;K, string&gt;</c> — every caller passes <c>getNodeId</c>.
    /// </remarks>
    internal static void AddToGroup<K, V>(OrderedDictionary<string, (K Key, List<V> Values)> map, K key, V value, Func<K, string> getKey)
        where K : Node
    {
        var keyString = getKey(key);
        if (map.TryGetValue(keyString, out var group))
        {
            group.Values.Add(value);
        }
        else
        {
            map[keyString] = (key, [value]);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44592-44594 — <c>tryGetRootParameterDeclaration</c>.</summary>
    internal static ParameterDeclaration? TryGetRootParameterDeclaration(Node node)
        => Core.TryCast<ParameterDeclaration, Node>(Utilities.GetRootDeclaration(node), NodeTests.IsParameter);

    /// <summary>ported from: src/compiler/checker.ts:44596-44609 — <c>isValidUnusedLocalDeclaration</c>.</summary>
    internal bool IsValidUnusedLocalDeclaration(IDeclaration declaration)
    {
        if (NodeTests.IsBindingElement((Node)declaration))
        {
            var bindingElement = (BindingElement)declaration;
            if (NodeTests.IsObjectBindingPattern(bindingElement.Parent!))
            {
                /**
                 * ignore starts with underscore names _
                 * const { a: _a } = { a: 1 }
                 */
                return bindingElement.PropertyName is not null && IsIdentifierThatStartsWithUnderscore(bindingElement.Name!);
            }
            return IsIdentifierThatStartsWithUnderscore(bindingElement.Name!);
        }
        return Utilities.IsAmbientModule((Node)declaration) ||
            (NodeTests.IsVariableDeclaration((Node)declaration) && Utilities.IsForInOrOfStatement(((Node)declaration).Parent!.Parent!) || IsImportedDeclaration((Node)declaration)) && IsIdentifierThatStartsWithUnderscore(((INamedDeclaration)declaration).Name!);
    }

    /// <summary>ported from: src/compiler/checker.ts:44611-44723 — <c>checkUnusedLocalsAndParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>HasLocals</c> union, the port's <see cref="ILocalsContainer"/>, and its three
    /// grouping maps are <c>OrderedDictionary</c>s so that the diagnostics arrive in the reference's order. The
    /// reference's <c>bindingPattern.elements</c> read is the switch below (the two binding-pattern classes declare the
    /// slot with different element types).
    /// </remarks>
    internal void CheckUnusedLocalsAndParameters(Node nodeWithLocals, AddUnusedDiagnostic addDiagnostic)
    {
        // Ideally we could use the ImportClause directly as a key, but must wait until we have full ES6 maps. So must store key along with value.
        var unusedImports = new OrderedDictionary<string, (ImportClause Key, List<Node> Values)>(StringComparer.Ordinal);
        var unusedDestructures = new OrderedDictionary<string, (Node Key, List<BindingElement> Values)>(StringComparer.Ordinal);
        var unusedVariables = new OrderedDictionary<string, (VariableDeclarationList Key, List<VariableDeclaration> Values)>(StringComparer.Ordinal);
        ((ILocalsContainer)nodeWithLocals).Locals!.ForEach((local, _) =>
        {
            // If it's purely a type parameter, ignore, will be checked in `checkUnusedTypeParameters`.
            // If it's a type parameter merged with a parameter, check if the parameter-side is used.
            if ((local.Flags & SymbolFlags.TypeParameter) != 0
                ? !((local.Flags & SymbolFlags.Variable) != 0 && ((local.IsReferenced ?? SymbolFlags.None) & SymbolFlags.Variable) == 0)
                : (local.IsReferenced ?? SymbolFlags.None) != 0 || local.ExportSymbol is not null)
            {
                return;
            }

            if (local.Declarations is not null)
            {
                foreach (var declaration in local.Declarations)
                {
                    if (IsValidUnusedLocalDeclaration(declaration))
                    {
                        continue;
                    }

                    if (IsImportedDeclaration((Node)declaration))
                    {
                        AddToGroup(unusedImports, ImportClauseFromImported((Node)declaration), (Node)declaration, static key => Utilities.GetNodeId(key).ToString());
                    }
                    else if (NodeTests.IsBindingElement((Node)declaration) && NodeTests.IsObjectBindingPattern(((Node)declaration).Parent!))
                    {
                        // In `{ a, ...b }, `a` is considered used since it removes a property from `b`. `b` may still be unused though.
                        var lastElement = Core.Last(((ObjectBindingPattern)((Node)declaration).Parent!).Elements!);
                        if (ReferenceEquals(declaration, lastElement) || ((BindingElement)lastElement).DotDotDotToken is null)
                        {
                            AddToGroup(unusedDestructures, ((Node)declaration).Parent!, (BindingElement)declaration, static key => Utilities.GetNodeId(key).ToString());
                        }
                    }
                    else if (NodeTests.IsVariableDeclaration((Node)declaration))
                    {
                        var blockScopeKind = GetCombinedNodeFlagsCached((Node)declaration) & NodeFlags.BlockScoped;
                        var name = (Node?)Utilities.GetNameOfDeclaration((Node)declaration);
                        if (blockScopeKind != NodeFlags.Using && blockScopeKind != NodeFlags.AwaitUsing || name is null || !IsIdentifierThatStartsWithUnderscore(name!))
                        {
                            AddToGroup(unusedVariables, (VariableDeclarationList)((Node)declaration).Parent!, (VariableDeclaration)declaration, static key => Utilities.GetNodeId(key).ToString());
                        }
                    }
                    else
                    {
                        var parameter = local.ValueDeclaration is not null ? TryGetRootParameterDeclaration((Node)local.ValueDeclaration) : null;
                        var name = local.ValueDeclaration is not null ? (Node?)Utilities.GetNameOfDeclaration((Node)local.ValueDeclaration) : null;
                        if (parameter is not null && name is not null)
                        {
                            if (!Utilities.IsParameterPropertyDeclaration(parameter, parameter.Parent!) && !Utilities.ParameterIsThisKeyword(parameter) && !IsIdentifierThatStartsWithUnderscore(name!))
                            {
                                if (NodeTests.IsBindingElement((Node)declaration) && NodeTests.IsArrayBindingPattern(((Node)declaration).Parent!))
                                {
                                    AddToGroup(unusedDestructures, ((Node)declaration).Parent!, (BindingElement)declaration, static key => Utilities.GetNodeId(key).ToString());
                                }
                                else
                                {
                                    addDiagnostic(parameter, UnusedKind.Parameter, Utilities.CreateDiagnosticForNode(name, Diagnostics._0_is_declared_but_its_value_is_never_read, Utilities.SymbolName(local)));
                                }
                            }
                        }
                        else
                        {
                            ErrorUnusedLocal(declaration, Utilities.SymbolName(local), addDiagnostic);
                        }
                    }
                }
            }
        });
        foreach (var (importClause, unuseds) in unusedImports.Values)
        {
            var importDecl = (Node)importClause.Parent!;
            var nDeclarations = (importClause.Name is not null ? 1 : 0) +
                (importClause.NamedBindings is not null
                    ? (importClause.NamedBindings.Kind == SyntaxKind.NamespaceImport ? 1 : ((NamedImports)importClause.NamedBindings).Elements!.Count)
                    : 0);
            if (nDeclarations == unuseds.Count)
            {
                addDiagnostic(
                    importDecl,
                    UnusedKind.Local,
                    unuseds.Count == 1
                        ? Utilities.CreateDiagnosticForNode(importDecl, Diagnostics._0_is_declared_but_its_value_is_never_read, Utilities.IdText(((INamedDeclaration)Core.First(unuseds)).Name!))
                        : Utilities.CreateDiagnosticForNode(importDecl, Diagnostics.All_imports_in_import_declaration_are_unused));
            }
            else
            {
                foreach (var unused in unuseds) ErrorUnusedLocal((IDeclaration)unused, Utilities.IdText(((INamedDeclaration)unused).Name!), addDiagnostic);
            }
        }
        foreach (var (bindingPattern, bindingElements) in unusedDestructures.Values)
        {
            var kind = TryGetRootParameterDeclaration(bindingPattern.Parent!) is not null ? UnusedKind.Parameter : UnusedKind.Local;
            var elementCount = bindingPattern.Kind == SyntaxKind.ArrayBindingPattern
                ? ((ArrayBindingPattern)bindingPattern).Elements!.Count
                : ((ObjectBindingPattern)bindingPattern).Elements!.Count;
            if (elementCount == bindingElements.Count)
            {
                if (bindingElements.Count == 1 && bindingPattern.Parent!.Kind == SyntaxKind.VariableDeclaration && bindingPattern.Parent!.Parent!.Kind == SyntaxKind.VariableDeclarationList)
                {
                    AddToGroup(unusedVariables, (VariableDeclarationList)bindingPattern.Parent!.Parent!, (VariableDeclaration)bindingPattern.Parent!, static key => Utilities.GetNodeId(key).ToString());
                }
                else
                {
                    addDiagnostic(
                        bindingPattern,
                        kind,
                        bindingElements.Count == 1
                            ? Utilities.CreateDiagnosticForNode(bindingPattern, Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(Core.First(bindingElements).Name!))
                            : Utilities.CreateDiagnosticForNode(bindingPattern, Diagnostics.All_destructured_elements_are_unused));
                }
            }
            else
            {
                foreach (var element in bindingElements)
                {
                    addDiagnostic(element, kind, Utilities.CreateDiagnosticForNode(element, Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(element.Name!)));
                }
            }
        }
        foreach (var (declarationList, declarations) in unusedVariables.Values)
        {
            if (declarationList.Declarations!.Count == declarations.Count)
            {
                addDiagnostic(
                    declarationList,
                    UnusedKind.Local,
                    declarations.Count == 1
                        ? Utilities.CreateDiagnosticForNode(Core.First(declarations).Name!, Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(Core.First(declarations).Name!))
                        : Utilities.CreateDiagnosticForNode(declarationList.Parent!.Kind == SyntaxKind.VariableStatement ? declarationList.Parent! : declarationList, Diagnostics.All_variables_are_unused));
            }
            else
            {
                foreach (var declaration in declarations)
                {
                    addDiagnostic(declaration, UnusedKind.Local, Utilities.CreateDiagnosticForNode(declaration, Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(declaration.Name!)));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44725-44741 — <c>checkPotentialUncheckedRenamedBindingElementsInTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>walkUpBindingElementsAndPatterns</c> answers <see cref="Node"/> in the port; the reference reads
    /// <c>wrappingDeclaration.type</c> off its <c>VariableDeclaration | ParameterDeclaration</c> answer, so the slot is
    /// read through those two classes.
    /// </remarks>
    internal void CheckPotentialUncheckedRenamedBindingElementsInTypes()
    {
        foreach (var node in _potentialUnusedRenamedBindingElementsInTypes)
        {
            if ((GetSymbolOfDeclaration(node).IsReferenced ?? SymbolFlags.None) == 0)
            {
                var wrappingDeclaration = Utilities.WalkUpBindingElementsAndPatterns(node);
                Debug.Assert(Utilities.IsPartOfParameterDeclaration(wrappingDeclaration), "Only parameter declaration should be checked here");
                var diagnostic = Utilities.CreateDiagnosticForNode(node.Name!, Diagnostics._0_is_an_unused_renaming_of_1_Did_you_intend_to_use_it_as_a_type_annotation, Utilities.DeclarationNameToString(node.Name!), Utilities.DeclarationNameToString(node.PropertyName!));
                var wrappingType = wrappingDeclaration.Kind == SyntaxKind.Parameter
                    ? ((ParameterDeclaration)wrappingDeclaration).Type
                    : ((VariableDeclaration)wrappingDeclaration).Type;
                if (wrappingType is null)
                {
                    // entire parameter does not have type annotation, suggest adding an annotation
                    Utilities.AddRelatedInfo(
                        diagnostic,
                        Utilities.CreateFileDiagnostic(Utilities.GetSourceFileOfNode(wrappingDeclaration)!, wrappingDeclaration.End, 0, Diagnostics.We_can_only_write_a_type_for_0_by_adding_a_type_for_the_entire_parameter_here, Utilities.DeclarationNameToString(node.PropertyName!)));
                }
                _diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44743-44753 — <c>bindingNameText</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>bindingNameText</c> falls out of its switch with <c>Debug.assertNever</c>, which returns <c>never</c>; the value-producing port form is <c>Debug.Fail&lt;string&gt;</c>.</remarks>
    internal static string BindingNameText(Node name)
    {
        switch (name.Kind)
        {
            case SyntaxKind.Identifier:
                return Utilities.IdText(name);
            case SyntaxKind.ArrayBindingPattern:
            case SyntaxKind.ObjectBindingPattern:
                {
                    var firstElement = name.Kind == SyntaxKind.ArrayBindingPattern
                        ? Core.First(((ArrayBindingPattern)name).Elements!)
                        : Core.First(((ObjectBindingPattern)name).Elements!);
                    return BindingNameText(Debug.CheckDefined(Core.TryCast<BindingElement, Node>(firstElement, NodeTests.IsBindingElement), "a binding pattern's first element is a binding element").Name!);
                }
            default:
                return Debug.Fail<string>("Unexpected binding name kind: " + Debug.FormatSyntaxKind(name.Kind));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44756-44758 — <c>isImportedDeclaration</c> (its <c>ImportedDeclaration</c> alias is at 44755).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's type predicate narrows to the <c>ImportedDeclaration</c> union, which the port has no C# type
    /// for; the port answers <c>bool</c> (the <c>Utilities.CanHaveLocals</c> treatment) and the two callers hand the
    /// same node to <see cref="ImportClauseFromImported"/>.
    /// </remarks>
    internal static bool IsImportedDeclaration(Node node)
        => node.Kind == SyntaxKind.ImportClause || node.Kind == SyntaxKind.ImportSpecifier || node.Kind == SyntaxKind.NamespaceImport;

    /// <summary>ported from: src/compiler/checker.ts:44759-44761 — <c>importClauseFromImported</c>.</summary>
    internal static ImportClause ImportClauseFromImported(Node decl)
        => decl.Kind == SyntaxKind.ImportClause
            ? (ImportClause)decl
            : decl.Kind == SyntaxKind.NamespaceImport
                ? (ImportClause)decl.Parent!
                : (ImportClause)decl.Parent!.Parent!;
}
