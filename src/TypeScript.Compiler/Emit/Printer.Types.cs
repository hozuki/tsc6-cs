namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:2032-2611 (the helpers, literals, snippet elements, identifiers/names,
//               signature elements, type members, the whole type-node family and the binding patterns) and
//               src/compiler/emitter.ts:5233-5794 (the text-of-node readers and the NameGenerator).
//
// PORT-NOTE: the reference's printer is one closure whose ~200 nested functions close over ~45 locals; the port states
// it as `Printer` (docs/architecture.md §17.3) and this file is the "type/name half" slice. Every method keeps the
// reference's name in PascalCase, its argument list and its body order.
//
// PORT-NOTE: three of the reference's union parameter types have no name in the port (§15.9's "a union with no common
// class falls back to `Node`"): `EntityName`, `DeclarationName`, `AccessorDeclaration` and `SnippetElement` arrive as
// `Node`/`object?` below, and each site says which member it reads. Where a *class* common base does exist
// (`FunctionTypeNode | ConstructorTypeNode` → `FunctionOrConstructorTypeNodeBase`, `NumericLiteral | BigIntLiteral` →
// `LiteralExpression`) the port uses it, since that is the same type the reference's cast would produce.
//
// PORT-NOTE: two of those collapsed unions arrive as *interfaces* — `ITypeNode` (TS's `TypeNode`) and `ITypeElement` — and
// so does the `LiteralLikeNode` face, while the emit family is stated over the `Node` class. C# converts an interface to a
// class only explicitly, so every call below that hands one to `Emit` writes `(Node?)…` (the conversion the reference's
// own generic parameter performed implicitly at compile time and its `as` performed at run time), and reading a range slot
// off one (`End`) goes through `(ITextRange)`.
//
// PORT-NOTE: the reference passes a parenthesizer method itself as the `parenthesizerRule` argument
// (`emit(node.type, parenthesizer.parenthesizeOperandOfTypeOperator)`); the port's rule slot is `Func<Node, Node>?`
// (the reference's `ParenthesizerRule<T>`), while `IParenthesizerRules`' members are typed on the concrete node classes
// (`ITypeNode`, `Expression`, …). C# has no conversion between the two, so every use site writes the rule as an
// implicit-parameter lambda that performs the same narrowing the reference's generic call would have performed; the
// lambda's body is the parenthesizer method, and its argument is the node the reference hands it.
//
// PORT-NOTE: `TempFlags` in `Printer.State.cs` — the cross-slice field contract this file must not edit — declares
// `CountMask = 0b1111` and has no `_i` member, while the reference's own `TempFlags` (emitter.ts:6345-6349) is
// `Auto = 0`, `CountMask = 0x0FFFFFFF`, `_i = 0x10000000`. The name generator below is the only reader, so it names the
// two reference values locally (see `TempFlagsI`/`TempFlagsCountMask`); the integration owner must fold them into the
// enum and delete the two constants.
internal sealed partial class Printer
{
    // ---------------------------------------------------------------------------------------------
    // A mapped type's parameter (emitter.ts:2032-2038) and the substitution phase (emitter.ts:2040-2046)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2032-2038 — <c>emitMappedTypeParameter</c>.</summary>
    private void EmitMappedTypeParameter(TypeParameterDeclaration node)
    {
        Emit(node.Name);
        WriteSpace();
        WriteKeyword("in");
        WriteSpace();
        Emit((Node?)node.Constraint);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2040-2046 — <c>pipelineEmitWithSubstitution</c>.</summary>
    private void PipelineEmitWithSubstitution(EmitHint hint, Node node)
    {
        var pipelinePhase = GetNextPipelinePhase(PipelinePhase.Substitution, hint, node);
        Debug.AssertIsDefined(_lastSubstitution);
        node = _lastSubstitution;
        _lastSubstitution = null;
        pipelinePhase(hint, node);
    }

    // ---------------------------------------------------------------------------------------------
    // Emit helpers (emitter.ts:2048-2105)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2048-2095 — <c>emitHelpers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's early <c>return;</c> (a bundle under <c>ModuleKind.None</c>) answers <c>undefined</c>
    /// to a caller that never reads the result; the port answers <c>false</c>, which is the same "nothing was emitted"
    /// the tail of the function reports.
    /// </remarks>
    private bool EmitHelpers(Node node)
    {
        var helpersEmitted = false;
        var bundle = node.Kind == SyntaxKind.Bundle ? (Bundle)node : null;
        if (bundle is not null && _moduleKind == ModuleKind.None)
        {
            return false;
        }

        var numNodes = bundle is not null ? bundle.SourceFiles!.Count : 1;
        for (var i = 0; i < numNodes; i++)
        {
            var currentNode = bundle is not null ? bundle.SourceFiles![i] : node;
            var sourceFile = NodeTests.IsSourceFile(currentNode) ? (SourceFile)currentNode : _currentSourceFile;
            var shouldSkip = Js.IsTruthy(_printerOptions.NoEmitHelpers) || (sourceFile is not null && FactoryUtilities.HasRecordedExternalHelpers(sourceFile));
            var shouldBundle = NodeTests.IsSourceFile(currentNode) && !_isOwnFileEmit;
            var helpers = GetSortedEmitHelpers(currentNode);
            if (helpers is not null)
            {
                foreach (var helper in helpers)
                {
                    if (!helper.Scoped)
                    {
                        // Skip the helper if it can be skipped and the noEmitHelpers compiler
                        // option is set, or if it can be imported and the importHelpers compiler
                        // option is set.
                        if (shouldSkip) continue;

                        // Skip the helper if it can be bundled but hasn't already been emitted and we
                        // are emitting a bundled module.
                        if (shouldBundle)
                        {
                            if (_bundledHelpers.ContainsKey(helper.Name!))
                            {
                                continue;
                            }

                            _bundledHelpers[helper.Name!] = true;
                        }
                    }
                    else if (bundle is not null)
                    {
                        // Skip the helper if it is scoped and we are emitting bundled helpers
                        continue;
                    }

                    if (helper.Text is string helperText)
                    {
                        WriteLines(helperText);
                    }
                    else
                    {
                        WriteLines(((Func<EmitHelperUniqueNameCallback, string>)helper.Text!)(MakeFileLevelOptimisticUniqueName));
                    }

                    helpersEmitted = true;
                }
            }
        }

        return helpersEmitted;
    }

    /// <summary>ported from: src/compiler/emitter.ts:2097-2100 — <c>getSortedEmitHelpers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>getEmitHelpers(node)</c> answers <c>EmitHelper[]</c>; the port's generated slot is
    /// <c>List&lt;Node&gt;?</c>, because <c>EmitHelper</c> derives from <c>Node</c> so one object fits every slot that
    /// takes a helper (see <c>Model/EmitHelper.cs</c>). The descriptors are therefore recovered with the cast that class
    /// note names — the reference would fail the same way on an entry that is not a descriptor, since the very next
    /// thing it does is read <c>helper.scoped</c>.
    /// </remarks>
    private IList<EmitHelper>? GetSortedEmitHelpers(Node node)
    {
        var helpers = EmitNodes.GetEmitHelpers(node);
        return helpers is null ? null : Core.ToSorted(helpers.Cast<EmitHelper>().ToList(), TypeScript.Compiler.EmitHelpers.CompareEmitHelpers);
    }

    // ---------------------------------------------------------------------------------------------
    // Literals/Pseudo-literals (emitter.ts:2108-2133)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2106-2110 — <c>emitNumericOrBigIntLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>NumericLiteral | BigIntLiteral</c>; the printer's dispatch
    /// (emitter.ts:1578/1932, <c>pipelineEmitWithHintWorker</c>) hands it the <c>Node</c> it matched on, which is what the
    /// port takes, and both members are <c>ILiteralLikeNode</c>, the interface <c>emitLiteral</c> is stated over.
    /// </remarks>
    private void EmitNumericOrBigIntLiteral(Node node) => EmitLiteral((ILiteralLikeNode)node, /*jsxAttributeEscape*/ false);

    /// <summary>ported from: src/compiler/emitter.ts:2112-2133 — <c>emitLiteral</c>.</summary>
    private void EmitLiteral(ILiteralLikeNode node, bool jsxAttributeEscape)
    {
        var text = GetLiteralTextOfNode(node, sourceFile: null, _printerOptions.NeverAsciiEscape, jsxAttributeEscape);
        if (
            (Js.IsTruthy(_printerOptions.SourceMap) || Js.IsTruthy(_printerOptions.InlineSourceMap))
            && (node.Kind == SyntaxKind.StringLiteral || Utilities.IsTemplateLiteralKind(node.Kind))
        )
        {
            WriteLiteral(text);
        }
        else
        {
            // Quick info expects all literals to be called with writeStringLiteral, as there's no specific type for numberLiterals
            WriteStringLiteral(text);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Snippet Elements (emitter.ts:2136-2162)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2136-2145 — <c>emitSnippetNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>SnippetElement</c> (<c>Placeholder | TabStop</c>) and it dispatches on
    /// <c>snippet.kind</c>. Neither member is a <c>Node</c>, the port has no implementation of either interface, and the
    /// model slot the caller reads is generated as <c>Node?</c> (<c>EmitNodes.GetSnippetElement</c>), so the parameter is
    /// <c>object?</c> and the two arms test the interfaces this port *does* declare — the same two kinds the reference's
    /// <c>case</c> labels name.
    /// </remarks>
    private void EmitSnippetNode(EmitHint hint, Node node, object? snippet)
    {
        switch (snippet)
        {
            case IPlaceholder placeholder:
                EmitPlaceholder(hint, node, placeholder);
                break;
            case ITabStop tabStop:
                EmitTabStop(hint, node, tabStop);
                break;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:2147-2152 — <c>emitPlaceholder</c>.</summary>
    private void EmitPlaceholder(EmitHint hint, Node node, IPlaceholder snippet)
    {
        NonEscapingWrite("${" + snippet.Order + ":"); // `${2:`
        PipelineEmitWithHintWorker(hint, node, /*allowSnippets*/ false); // `...`
        NonEscapingWrite("}"); // `}`
        // `${2:...}`
    }

    /// <summary>ported from: src/compiler/emitter.ts:2154-2162 — <c>emitTabStop</c>.</summary>
    private void EmitTabStop(EmitHint hint, Node node, ITabStop snippet)
    {
        // A tab stop should only be attached to an empty node, i.e. a node that doesn't emit any text.
        Debug.Assert(node.Kind == SyntaxKind.EmptyStatement, $"A tab stop cannot be attached to a node of kind {Debug.FormatSyntaxKind(node.Kind)}.");
        Debug.Assert(hint != EmitHint.EmbeddedStatement, "A tab stop cannot be attached to an embedded statement.");
        NonEscapingWrite("$" + snippet.Order);
    }

    // ---------------------------------------------------------------------------------------------
    // Identifiers (emitter.ts:2165-2172)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2165-2172 — <c>emitIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference binds <c>writeText</c> to one of two functions by truthiness of <c>node.symbol</c> and
    /// then calls it with the symbol in both arms; the port's two writers take different argument lists, so the branch is
    /// on the call. `writer.writeSymbol(text, symbol)` is <c>IEmitTextWriter.WriteSymbol</c>, whose symbol argument the
    /// generated model types <c>ISymbol?</c>.
    /// </remarks>
    private void EmitIdentifier(Identifier node)
    {
        var text = GetTextOfNode(node, /*includeTrivia*/ false);
        if (node.Symbol is not null)
        {
            WriteSymbol(text, node.Symbol);
        }
        else
        {
            Write(text);
        }

        EmitList(node, EmitNodes.GetIdentifierTypeArguments(node), ListFormat.TypeParameters); // Call emitList directly since it could be an array of TypeParameterDeclarations _or_ type arguments
    }

    // ---------------------------------------------------------------------------------------------
    // Names (emitter.ts:2175-2200)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2175-2177 — <c>emitPrivateIdentifier</c>.</summary>
    private void EmitPrivateIdentifier(PrivateIdentifier node) => Write(GetTextOfNode(node, /*includeTrivia*/ false));

    /// <summary>ported from: src/compiler/emitter.ts:2179-2183 — <c>emitQualifiedName</c>.</summary>
    private void EmitQualifiedName(QualifiedName node)
    {
        EmitEntityName(node.Left!);
        WritePunctuation(".");
        Emit(node.Right);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2185-2192 — <c>emitEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>EntityName</c> (<c>Identifier | QualifiedName</c>), which has no C#
    /// name; the port takes the <c>Node</c> the union falls back to, exactly as the reference's <c>node.kind</c> test
    /// already discriminates.
    /// </remarks>
    private void EmitEntityName(Node node)
    {
        if (node.Kind == SyntaxKind.Identifier)
        {
            EmitExpression(node);
        }
        else
        {
            Emit(node);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:2194-2198 — <c>emitComputedPropertyName</c>.</summary>
    private void EmitComputedPropertyName(ComputedPropertyName node)
    {
        WritePunctuation("[");
        EmitExpression(node.Expression, n => _parenthesizer.ParenthesizeExpressionOfComputedPropertyName((Expression)n));
        WritePunctuation("]");
    }

    // ---------------------------------------------------------------------------------------------
    // Signature elements (emitter.ts:2204-2241)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2204-2219 — <c>emitTypeParameter</c>.</summary>
    private void EmitTypeParameter(TypeParameterDeclaration node)
    {
        EmitModifierList(node, node.Modifiers);
        Emit(node.Name);
        if (node.Constraint is not null)
        {
            WriteSpace();
            WriteKeyword("extends");
            WriteSpace();
            Emit((Node?)node.Constraint);
        }
        if (node.Default is not null)
        {
            WriteSpace();
            WriteOperator("=");
            WriteSpace();
            Emit((Node?)node.Default);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:2221-2234 — <c>emitParameter</c>.</summary>
    private void EmitParameter(ParameterDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ true);
        Emit(node.DotDotDotToken);
        EmitNodeWithWriter(node.Name, WriteParameter);
        Emit(node.QuestionToken);
        if (node.Parent is not null && node.Parent.Kind == SyntaxKind.JSDocFunctionType && node.Name is null)
        {
            Emit((Node?)node.Type);
        }
        else
        {
            EmitTypeAnnotation(node.Type);
        }
        // The comment position has to fallback to any present node within the parameterdeclaration because as it turns out, the parser can make parameter declarations with _just_ an initializer.
        EmitInitializer(node.Initializer, node.Type is not null ? ((ITextRange)node.Type!).End : node.QuestionToken is not null ? node.QuestionToken.End : node.Name is not null ? node.Name.End : node.Modifiers is not null ? node.Modifiers.End : node.Pos, node, n => _parenthesizer.ParenthesizeExpressionForDisallowedComma((Expression)n));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2236-2239 — <c>emitDecorator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference passes <c>parenthesizeLeftSideOfAccess</c> itself, leaving its optional
    /// <c>optionalChain</c> argument <c>undefined</c>; the port's rule is a lambda, so that argument is written
    /// <c>null</c>.
    /// </remarks>
    private void EmitDecorator(Decorator decorator)
    {
        WritePunctuation("@");
        EmitExpression(decorator.Expression, n => _parenthesizer.ParenthesizeLeftSideOfAccess((Expression)n, null));
    }

    // ---------------------------------------------------------------------------------------------
    // Type members (emitter.ts:2245-2326)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2245-2251 — <c>emitPropertySignature</c>.</summary>
    private void EmitPropertySignature(PropertySignature node)
    {
        EmitModifierList(node, node.Modifiers);
        EmitNodeWithWriter(node.Name, WriteProperty);
        Emit(node.QuestionToken);
        EmitTypeAnnotation(node.Type);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:2253-2261 — <c>emitPropertyDeclaration</c>.</summary>
    private void EmitPropertyDeclaration(PropertyDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ true);
        Emit(node.Name);
        Emit(node.QuestionToken);
        Emit(node.ExclamationToken);
        EmitTypeAnnotation(node.Type);
        EmitInitializer(node.Initializer, node.Type is not null ? ((ITextRange)node.Type!).End : node.QuestionToken is not null ? node.QuestionToken.End : node.Name!.End, node);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:2263-2268 — <c>emitMethodSignature</c>.</summary>
    private void EmitMethodSignature(MethodSignature node)
    {
        EmitModifierList(node, node.Modifiers);
        Emit(node.Name);
        Emit(node.QuestionToken);
        EmitSignatureAndBody(node, n => EmitSignatureHead(n), n => EmitEmptyFunctionBody(n));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2270-2276 — <c>emitMethodDeclaration</c>.</summary>
    private void EmitMethodDeclaration(MethodDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ true);
        Emit(node.AsteriskToken);
        Emit(node.Name);
        Emit(node.QuestionToken);
        EmitSignatureAndBody(node, n => EmitSignatureHead(n), n => EmitFunctionBody(n));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2278-2283 — <c>emitClassStaticBlockDeclaration</c>.</summary>
    private void EmitClassStaticBlockDeclaration(ClassStaticBlockDeclaration node)
    {
        WriteKeyword("static");
        PushNameGenerationScope(node);
        EmitBlockFunctionBody(node.Body!);
        PopNameGenerationScope(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2285-2289 — <c>emitConstructor</c>.</summary>
    private void EmitConstructor(ConstructorDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        WriteKeyword("constructor");
        EmitSignatureAndBody(node, n => EmitSignatureHead(n), n => EmitFunctionBody(n));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2291-2298 — <c>emitAccessorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>AccessorDeclaration</c> (<c>GetAccessorDeclaration |
    /// SetAccessorDeclaration</c>), which has no C# name; the port takes the <c>Node</c> it falls back to and reads the
    /// two slots the body uses (<c>modifiers</c>, <c>name</c>) through the kind test the reference's own
    /// <c>node.kind === SyntaxKind.GetAccessor</c> already performs. The return value is the position
    /// <c>emitDecoratorsAndModifiers</c> answers, which the reference forwards to <c>emitTokenWithComment</c>.
    /// </remarks>
    private int EmitAccessorDeclaration(Node node)
    {
        var pos = EmitDecoratorsAndModifiers(node, node is GetAccessorDeclaration getAccessor ? getAccessor.Modifiers : ((SetAccessorDeclaration)node).Modifiers, /*allowDecorators*/ true);
        var token = node.Kind == SyntaxKind.GetAccessor ? SyntaxKind.GetKeyword : SyntaxKind.SetKeyword;
        EmitTokenWithComment(token, pos, WriteKeyword, node);
        WriteSpace();
        Emit(((INamedDeclaration)node).Name);
        EmitSignatureAndBody(node, n => EmitSignatureHead(n), n => EmitFunctionBody(n));
        return pos;
    }

    /// <summary>ported from: src/compiler/emitter.ts:2300-2302 — <c>emitCallSignature</c>.</summary>
    private void EmitCallSignature(CallSignatureDeclaration node) => EmitSignatureAndBody(node, n => EmitSignatureHead(n), n => EmitEmptyFunctionBody(n));

    /// <summary>ported from: src/compiler/emitter.ts:2304-2308 — <c>emitConstructSignature</c>.</summary>
    private void EmitConstructSignature(ConstructSignatureDeclaration node)
    {
        WriteKeyword("new");
        WriteSpace();
        EmitSignatureAndBody(node, n => EmitSignatureHead(n), n => EmitEmptyFunctionBody(n));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2310-2315 — <c>emitIndexSignature</c>.</summary>
    private void EmitIndexSignature(IndexSignatureDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        EmitParametersForIndexSignature(node, node.Parameters!);
        EmitTypeAnnotation(node.Type);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:2317-2320 — <c>emitTemplateTypeSpan</c>.</summary>
    private void EmitTemplateTypeSpan(TemplateLiteralTypeSpan node)
    {
        Emit((Node?)node.Type);
        Emit(node.Literal);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2322-2324 — <c>emitSemicolonClassElement</c>.</summary>
    private void EmitSemicolonClassElement() => WriteTrailingSemicolon();

    // ---------------------------------------------------------------------------------------------
    // Types (emitter.ts:2330-2582)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2330-2342 — <c>emitTypePredicate</c>.</summary>
    private void EmitTypePredicate(TypePredicateNode node)
    {
        if (node.AssertsModifier is not null)
        {
            Emit(node.AssertsModifier);
            WriteSpace();
        }
        Emit(node.ParameterName);
        if (node.Type is not null)
        {
            WriteSpace();
            WriteKeyword("is");
            WriteSpace();
            Emit((Node?)node.Type);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:2344-2347 — <c>emitTypeReference</c>.</summary>
    private void EmitTypeReference(TypeReferenceNode node)
    {
        Emit(node.TypeName);
        EmitTypeArguments(node, node.TypeArguments);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2349-2351 — <c>emitFunctionType</c>.</summary>
    private void EmitFunctionType(FunctionTypeNode node) => EmitSignatureAndBody(node, n => EmitFunctionTypeHead((FunctionOrConstructorTypeNodeBase)n), n => EmitFunctionTypeBody((FunctionOrConstructorTypeNodeBase)n));

    /// <summary>ported from: src/compiler/emitter.ts:2353-2358 — <c>emitFunctionTypeHead</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>FunctionTypeNode | ConstructorTypeNode</c>; the port names its class
    /// common base, <c>FunctionOrConstructorTypeNodeBase</c>.
    /// </remarks>
    private void EmitFunctionTypeHead(FunctionOrConstructorTypeNodeBase node)
    {
        EmitTypeParameters(node, node.TypeParameters);
        EmitParametersForArrow(node, node.Parameters!);
        WriteSpace();
        WritePunctuation("=>");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2360-2363 — <c>emitFunctionTypeBody</c>.</summary>
    private void EmitFunctionTypeBody(FunctionOrConstructorTypeNodeBase node)
    {
        WriteSpace();
        Emit((Node?)node.Type);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2365-2370 — <c>emitJSDocFunctionType</c>.</summary>
    private void EmitJSDocFunctionType(JSDocFunctionType node)
    {
        WriteKeyword("function");
        EmitParameters(node, node.Parameters!);
        WritePunctuation(":");
        Emit((Node?)node.Type);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2372-2375 — <c>emitJSDocNullableType</c>.</summary>
    private void EmitJSDocNullableType(JSDocNullableType node)
    {
        WritePunctuation("?");
        Emit((Node?)node.Type);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2377-2380 — <c>emitJSDocNonNullableType</c>.</summary>
    private void EmitJSDocNonNullableType(JSDocNonNullableType node)
    {
        WritePunctuation("!");
        Emit((Node?)node.Type);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2382-2385 — <c>emitJSDocOptionalType</c>.</summary>
    private void EmitJSDocOptionalType(JSDocOptionalType node)
    {
        Emit((Node?)node.Type);
        WritePunctuation("=");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2387-2392 — <c>emitConstructorType</c>.</summary>
    private void EmitConstructorType(ConstructorTypeNode node)
    {
        EmitModifierList(node, node.Modifiers);
        WriteKeyword("new");
        WriteSpace();
        EmitSignatureAndBody(node, n => EmitFunctionTypeHead((FunctionOrConstructorTypeNodeBase)n), n => EmitFunctionTypeBody((FunctionOrConstructorTypeNodeBase)n));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2394-2399 — <c>emitTypeQuery</c>.</summary>
    private void EmitTypeQuery(TypeQueryNode node)
    {
        WriteKeyword("typeof");
        WriteSpace();
        Emit(node.ExprName);
        EmitTypeArguments(node, node.TypeArguments);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2401-2411 — <c>emitTypeLiteral</c>.</summary>
    private void EmitTypeLiteral(TypeLiteralNode node)
    {
        PushNameGenerationScope(node);
        Core.ForEach(node.Members, (ITypeElement member, int _) =>
        {
            GenerateMemberNames((Node)member);
            return (object?)null;
        });

        WritePunctuation("{");
        var flags = (Utilities.GetEmitFlags(node) & EmitFlags.SingleLine) != 0 ? ListFormat.SingleLineTypeLiteralMembers : ListFormat.MultiLineTypeLiteralMembers;
        EmitList(node, node.Members, flags | ListFormat.NoSpaceIfEmpty);
        WritePunctuation("}");

        PopNameGenerationScope(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2413-2417 — <c>emitArrayType</c>.</summary>
    private void EmitArrayType(ArrayTypeNode node)
    {
        Emit((Node?)node.ElementType, n => (Node)_parenthesizer.ParenthesizeNonArrayTypeOfPostfixType((ITypeNode)n));
        WritePunctuation("[");
        WritePunctuation("]");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2419-2422 — <c>emitRestOrJSDocVariadicType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>RestTypeNode | JSDocVariadicType</c>, whose port classes share no base
    /// below <c>Node</c>; the body reads only <c>type</c>, which both declare as <c>ITypeNode?</c>.
    /// </remarks>
    private void EmitRestOrJSDocVariadicType(Node node)
    {
        WritePunctuation("...");
        Emit((Node?)(node is RestTypeNode restType ? restType.Type : ((JSDocVariadicType)node).Type));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2424-2429 — <c>emitTupleType</c>.</summary>
    private void EmitTupleType(TupleTypeNode node)
    {
        EmitTokenWithComment(SyntaxKind.OpenBracketToken, node.Pos, WritePunctuation, node);
        var flags = (Utilities.GetEmitFlags(node) & EmitFlags.SingleLine) != 0 ? ListFormat.SingleLineTupleTypeElements : ListFormat.MultiLineTupleTypeElements;
        EmitList(node, node.Elements, flags | ListFormat.NoSpaceIfEmpty, n => _parenthesizer.ParenthesizeElementTypeOfTupleType((Node?)n));
        EmitTokenWithComment(SyntaxKind.CloseBracketToken, node.Elements!.End, WritePunctuation, node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2431-2438 — <c>emitNamedTupleMember</c>.</summary>
    private void EmitNamedTupleMember(NamedTupleMember node)
    {
        Emit(node.DotDotDotToken);
        Emit(node.Name);
        Emit(node.QuestionToken);
        EmitTokenWithComment(SyntaxKind.ColonToken, node.Name!.End, WritePunctuation, node);
        WriteSpace();
        Emit((Node?)node.Type);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2440-2443 — <c>emitOptionalType</c>.</summary>
    private void EmitOptionalType(OptionalTypeNode node)
    {
        Emit((Node?)node.Type, n => (Node)_parenthesizer.ParenthesizeTypeOfOptionalType((ITypeNode)n));
        WritePunctuation("?");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2445-2447 — <c>emitUnionType</c>.</summary>
    private void EmitUnionType(UnionTypeNode node) => EmitList(node, node.Types, ListFormat.UnionTypeConstituents, n => (Node)_parenthesizer.ParenthesizeConstituentTypeOfUnionType((ITypeNode)n));

    /// <summary>ported from: src/compiler/emitter.ts:2449-2451 — <c>emitIntersectionType</c>.</summary>
    private void EmitIntersectionType(IntersectionTypeNode node) => EmitList(node, node.Types, ListFormat.IntersectionTypeConstituents, n => (Node)_parenthesizer.ParenthesizeConstituentTypeOfIntersectionType((ITypeNode)n));

    /// <summary>ported from: src/compiler/emitter.ts:2453-2467 — <c>emitConditionalType</c>.</summary>
    private void EmitConditionalType(ConditionalTypeNode node)
    {
        Emit((Node?)node.CheckType, n => (Node)_parenthesizer.ParenthesizeCheckTypeOfConditionalType((ITypeNode)n));
        WriteSpace();
        WriteKeyword("extends");
        WriteSpace();
        Emit((Node?)node.ExtendsType, n => (Node)_parenthesizer.ParenthesizeExtendsTypeOfConditionalType((ITypeNode)n));
        WriteSpace();
        WritePunctuation("?");
        WriteSpace();
        Emit((Node?)node.TrueType);
        WriteSpace();
        WritePunctuation(":");
        WriteSpace();
        Emit((Node?)node.FalseType);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2469-2473 — <c>emitInferType</c>.</summary>
    private void EmitInferType(InferTypeNode node)
    {
        WriteKeyword("infer");
        WriteSpace();
        Emit(node.TypeParameter);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2475-2479 — <c>emitParenthesizedType</c>.</summary>
    private void EmitParenthesizedType(ParenthesizedTypeNode node)
    {
        WritePunctuation("(");
        Emit((Node?)node.Type);
        WritePunctuation(")");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2481-2483 — <c>emitThisType</c>.</summary>
    private void EmitThisType() => WriteKeyword("this");

    /// <summary>ported from: src/compiler/emitter.ts:2485-2493 — <c>emitTypeOperator</c>.</summary>
    private void EmitTypeOperator(TypeOperatorNode node)
    {
        WriteTokenText(node.Operator, WriteKeyword);
        WriteSpace();

        Func<Node, Node> parenthesizerRule = node.Operator == SyntaxKind.ReadonlyKeyword
            ? n => (Node)_parenthesizer.ParenthesizeOperandOfReadonlyTypeOperator((ITypeNode)n)
            : n => (Node)_parenthesizer.ParenthesizeOperandOfTypeOperator((ITypeNode)n);
        Emit((Node?)node.Type, parenthesizerRule);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2495-2500 — <c>emitIndexedAccessType</c>.</summary>
    private void EmitIndexedAccessType(IndexedAccessTypeNode node)
    {
        Emit((Node?)node.ObjectType, n => (Node)_parenthesizer.ParenthesizeNonArrayTypeOfPostfixType((ITypeNode)n));
        WritePunctuation("[");
        Emit((Node?)node.IndexType);
        WritePunctuation("]");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2502-2549 — <c>emitMappedType</c>.</summary>
    private void EmitMappedType(MappedTypeNode node)
    {
        var emitFlags = Utilities.GetEmitFlags(node);
        WritePunctuation("{");
        if ((emitFlags & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
            IncreaseIndent();
        }
        if (node.ReadonlyToken is not null)
        {
            Emit(node.ReadonlyToken);
            if (node.ReadonlyToken.Kind != SyntaxKind.ReadonlyKeyword)
            {
                WriteKeyword("readonly");
            }
            WriteSpace();
        }
        WritePunctuation("[");

        PipelineEmit(EmitHint.MappedTypeParameter, node.TypeParameter!);
        if (node.NameType is not null)
        {
            WriteSpace();
            WriteKeyword("as");
            WriteSpace();
            Emit((Node?)node.NameType);
        }

        WritePunctuation("]");
        if (node.QuestionToken is not null)
        {
            Emit(node.QuestionToken);
            if (node.QuestionToken.Kind != SyntaxKind.QuestionToken)
            {
                WritePunctuation("?");
            }
        }
        WritePunctuation(":");
        WriteSpace();
        Emit((Node?)node.Type);
        WriteTrailingSemicolon();
        if ((emitFlags & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
            DecreaseIndent();
        }
        EmitList(node, node.Members, ListFormat.PreserveLines);
        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2551-2553 — <c>emitLiteralType</c>.</summary>
    private void EmitLiteralType(LiteralTypeNode node) => EmitExpression(node.Literal);

    /// <summary>ported from: src/compiler/emitter.ts:2555-2558 — <c>emitTemplateType</c>.</summary>
    private void EmitTemplateType(TemplateLiteralTypeNode node)
    {
        Emit(node.Head);
        EmitList(node, node.TemplateSpans, ListFormat.TemplateExpressionSpans);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2560-2583 — <c>emitImportTypeNode</c>.</summary>
    private void EmitImportTypeNode(ImportTypeNode node)
    {
        if (node.IsTypeOf)
        {
            WriteKeyword("typeof");
            WriteSpace();
        }
        WriteKeyword("import");
        WritePunctuation("(");
        Emit((Node?)node.Argument);
        if (node.Attributes is not null)
        {
            WritePunctuation(",");
            WriteSpace();
            PipelineEmit(EmitHint.ImportTypeNodeAttributes, node.Attributes);
        }
        WritePunctuation(")");
        if (node.Qualifier is not null)
        {
            WritePunctuation(".");
            Emit(node.Qualifier);
        }
        EmitTypeArguments(node, node.TypeArguments);
    }

    // ---------------------------------------------------------------------------------------------
    // Binding patterns (emitter.ts:2585-2609)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:2585-2589 — <c>emitObjectBindingPattern</c>.</summary>
    private void EmitObjectBindingPattern(ObjectBindingPattern node)
    {
        WritePunctuation("{");
        EmitList(node, node.Elements, ListFormat.ObjectBindingPatternElements);
        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2591-2595 — <c>emitArrayBindingPattern</c>.</summary>
    private void EmitArrayBindingPattern(ArrayBindingPattern node)
    {
        WritePunctuation("[");
        EmitList(node, node.Elements, ListFormat.ArrayBindingPatternElements);
        WritePunctuation("]");
    }

    /// <summary>ported from: src/compiler/emitter.ts:2597-2607 — <c>emitBindingElement</c>.</summary>
    private void EmitBindingElement(BindingElement node)
    {
        Emit(node.DotDotDotToken);
        if (node.PropertyName is not null)
        {
            Emit(node.PropertyName);
            WritePunctuation(":");
            WriteSpace();
        }
        Emit(node.Name);
        EmitInitializer(node.Initializer, node.Name!.End, node, n => _parenthesizer.ParenthesizeExpressionForDisallowedComma((Expression)n));
    }

    // =============================================================================================
    // The name generator and the text-of-node readers (emitter.ts:5233-5794)
    // =============================================================================================

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5233-5277 — the text of a node
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5233-5239 — <c>skipSynthesizedParentheses</c>.</summary>
    private static Node SkipSynthesizedParentheses(Node node)
    {
        while (node.Kind == SyntaxKind.ParenthesizedExpression && Utilities.NodeIsSynthesized(node))
        {
            node = ((ParenthesizedExpression)node).Expression!;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5241-5267 — <c>getTextOfNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>Identifier | PrivateIdentifier | LiteralExpression |
    /// JsxNamespacedName</c>, which has no C# name; the body discriminates with <c>isMemberName</c> /
    /// <c>isJsxNamespacedName</c> / <c>isLiteralExpression</c> exactly as the reference does, so the port takes the
    /// <c>Node</c> the union falls back to. The final read is the one place the port needs the non-null assertion the
    /// reference gets from its control flow: every arm above returns when <c>canUseSourceFile</c> is false, so
    /// <c>sourceFile</c> is defined on the last line.
    /// </remarks>
    private string GetTextOfNode(Node node, bool includeTrivia = false)
    {
        if (Utilities.IsGeneratedIdentifier(node) || Utilities.IsGeneratedPrivateIdentifier(node))
        {
            return GenerateName(node);
        }
        if (NodeTests.IsStringLiteral(node) && ((StringLiteral)node).TextSourceNode is not null)
        {
            return GetTextOfNode(((StringLiteral)node).TextSourceNode!, includeTrivia);
        }
        var sourceFile = _currentSourceFile; // needed for control flow
        var canUseSourceFile = sourceFile is not null && node.Parent is not null && !Utilities.NodeIsSynthesized(node);
        if (Utilities.IsMemberName(node))
        {
            if (!canUseSourceFile || !ReferenceEquals(Utilities.GetSourceFileOfNode(node), Utilities.GetOriginalNode(sourceFile)))
            {
                return Utilities.IdText(node);
            }
        }
        else if (NodeTests.IsJsxNamespacedName(node))
        {
            if (!canUseSourceFile || !ReferenceEquals(Utilities.GetSourceFileOfNode(node), Utilities.GetOriginalNode(sourceFile)))
            {
                return Utilities.GetTextOfJsxNamespacedName((JsxNamespacedName)node);
            }
        }
        else
        {
            Debug.AssertNode(node, Utilities.IsLiteralExpression); // not strictly necessary
            if (!canUseSourceFile)
            {
                return ((ILiteralLikeNode)node).Text!;
            }
        }
        return Utilities.GetSourceTextOfNodeFromSourceFile(sourceFile!, node, includeTrivia);
    }

    /// <summary>ported from: src/compiler/emitter.ts:5269-5292 — <c>getLiteralTextOfNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference defaults the second parameter with <c>sourceFile = currentSourceFile</c>, and its first
    /// caller (<c>emitLiteral</c>, emitter.ts:2119) passes <c>undefined</c> for it to mean exactly that default; the port
    /// repeats the default by coalescing a null argument, so both spellings land on the current source file.
    /// <para>
    /// PORT-NOTE: the reference names three text escapers of <c>utilities.ts</c> and one flag enum;
    /// <c>escapeString</c> is already the port's <c>Utilities.EscapeString</c> (utilities.ts:6207),
    /// <c>escapeNonAsciiString</c> and <c>escapeJsxAttributeString</c> landed with this milestone in
    /// <c>Utilities/Utilities.Printer.cs</c> (utilities.ts:6216-6258), as did <c>GetLiteralTextFlags</c> and
    /// <c>getLiteralText</c> (utilities.ts:1971-2033) — nothing in the port declared them, and this is the member whose
    /// faithful body needs them (docs/architecture.md §15.9).
    /// </para>
    /// </remarks>
    private string GetLiteralTextOfNode(ILiteralLikeNode node, SourceFile? sourceFile = null, bool? neverAsciiEscape = null, bool jsxAttributeEscape = false)
    {
        sourceFile ??= _currentSourceFile;
        if (node.Kind == SyntaxKind.StringLiteral && ((StringLiteral)node).TextSourceNode is not null)
        {
            var textSourceNode = ((StringLiteral)node).TextSourceNode!;
            if (NodeTests.IsIdentifier(textSourceNode) || NodeTests.IsPrivateIdentifier(textSourceNode) || NodeTests.IsNumericLiteral(textSourceNode) || NodeTests.IsJsxNamespacedName(textSourceNode))
            {
                var text = NodeTests.IsNumericLiteral(textSourceNode) ? ((NumericLiteral)textSourceNode).Text! : GetTextOfNode(textSourceNode);
                return jsxAttributeEscape
                    ? $"\"{Utilities.EscapeJsxAttributeString(text)}\""
                    : Js.IsTruthy(neverAsciiEscape) || (Utilities.GetEmitFlags((Node)node) & EmitFlags.NoAsciiEscaping) != 0
                        ? $"\"{Utilities.EscapeString(text)}\""
                        : $"\"{Utilities.EscapeNonAsciiString(text)}\"";
            }
            else
            {
                return GetLiteralTextOfNode((ILiteralLikeNode)textSourceNode, Utilities.GetSourceFileOfNode(textSourceNode), neverAsciiEscape, jsxAttributeEscape);
            }
        }

        var flags = (Js.IsTruthy(neverAsciiEscape) ? GetLiteralTextFlags.NeverAsciiEscape : GetLiteralTextFlags.None)
            | (jsxAttributeEscape ? GetLiteralTextFlags.JsxAttributeEscape : GetLiteralTextFlags.None)
            | (Js.IsTruthy(_printerOptions.TerminateUnterminatedLiterals) ? GetLiteralTextFlags.TerminateUnterminatedLiterals : GetLiteralTextFlags.None)
            | (_printerOptions.Target is not null && _printerOptions.Target >= ScriptTarget.ES2021 ? GetLiteralTextFlags.AllowNumericSeparator : GetLiteralTextFlags.None);

        return Utilities.GetLiteralText(node, sourceFile, flags);
    }

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5294-5338 — the name generation scopes
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5294-5311 — <c>pushNameGenerationScope</c>.</summary>
    private void PushNameGenerationScope(Node? node)
    {
        _privateNameTempFlagsStack.Push(_privateNameTempFlags);
        _privateNameTempFlags = TempFlags.Auto;
        _reservedPrivateNamesStack.Push(_reservedPrivateNames);

        if (node is not null && (Utilities.GetEmitFlags(node) & EmitFlags.ReuseTempVariableScope) != 0)
        {
            return;
        }

        _tempFlagsStack.Push(_tempFlags);
        _tempFlags = TempFlags.Auto;
        _formattedNameTempFlagsStack.Push(_formattedNameTempFlags);
        _formattedNameTempFlags = null;
        _reservedNamesStack.Push(_reservedNames);
    }

    /// <summary>ported from: src/compiler/emitter.ts:5313-5324 — <c>popNameGenerationScope</c>.</summary>
    private void PopNameGenerationScope(Node? node)
    {
        _privateNameTempFlags = _privateNameTempFlagsStack.Pop();
        _reservedPrivateNames = _reservedPrivateNamesStack.Pop();

        if (node is not null && (Utilities.GetEmitFlags(node) & EmitFlags.ReuseTempVariableScope) != 0)
        {
            return;
        }

        _tempFlags = _tempFlagsStack.Pop();
        _formattedNameTempFlags = _formattedNameTempFlagsStack.Pop();
        _reservedNames = _reservedNamesStack.Pop();
    }

    /// <summary>ported from: src/compiler/emitter.ts:5326-5332 — <c>reserveNameInNestedScopes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>lastOrUndefined(reservedNamesStack)</c> reads the last element of a JS array that is
    /// used as a stack; <c>Stack&lt;T&gt;.Peek()</c> is that element, and <c>Core.LastOrUndefined</c> cannot be used here
    /// because it takes an <c>IList</c> (a <c>Stack</c> is not one). The comparison is the reference's identity test
    /// (<c>===</c>), which is <c>ReferenceEquals</c>.
    /// </remarks>
    private void ReserveNameInNestedScopes(string name)
    {
        var lastReservedNames = _reservedNamesStack.Count > 0 ? _reservedNamesStack.Peek() : null;
        if (_reservedNames is null || ReferenceEquals(_reservedNames, lastReservedNames))
        {
            _reservedNames = [];
        }
        _reservedNames.Add(name);
    }

    /// <summary>ported from: src/compiler/emitter.ts:5333-5339 — <c>reservePrivateNameInNestedScopes</c>.</summary>
    private void ReservePrivateNameInNestedScopes(string name)
    {
        var lastReservedPrivateNames = _reservedPrivateNamesStack.Count > 0 ? _reservedPrivateNamesStack.Peek() : null;
        if (_reservedPrivateNames is null || ReferenceEquals(_reservedPrivateNames, lastReservedPrivateNames))
        {
            _reservedPrivateNames = [];
        }
        _reservedPrivateNames.Add(name);
    }

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5340-5453 — collecting the names a scope must generate
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5340-5424 — <c>generateNames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>case</c> arms cast to the several members of a union and read the one slot the arm
    /// needs; the port has no name for those unions, and its statement-valued slots are typed <c>IStatement</c> (an
    /// interface, not a <c>Node</c>), so the arms read through the concrete classes and cast to <c>Node</c> at the
    /// boundary — the same reads, with C#'s explicit conversions where TS's union casts stand. The two binding-pattern
    /// arms cannot share one `forEach` because `ObjectBindingPattern.Elements` and `ArrayBindingPattern.Elements` have
    /// different element types in the port (the reference's `BindingPattern` union has no C# name), so the arm tests the
    /// kind once and iterates each list at its own element type.
    /// </remarks>
    private void GenerateNames(Node? node)
    {
        if (node is null) return;
        switch (node.Kind)
        {
            case SyntaxKind.Block:
                Core.ForEach(((Block)node).Statements, (IStatement statement, int _) =>
                {
                    GenerateNames((Node)statement);
                    return (object?)null;
                });
                break;
            case SyntaxKind.LabeledStatement:
            case SyntaxKind.WithStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
                GenerateNames((Node?)(node is LabeledStatement labeledStatement ? labeledStatement.Statement
                    : node is WithStatement withStatement ? withStatement.Statement
                    : ((IterationStatement)node).Statement));
                break;
            case SyntaxKind.IfStatement:
                GenerateNames((Node?)((IfStatement)node).ThenStatement);
                GenerateNames((Node?)((IfStatement)node).ElseStatement);
                break;
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.ForInStatement:
                GenerateNames(node is ForStatement forStatement ? forStatement.Initializer
                    : node is ForInStatement forInStatement ? forInStatement.Initializer
                    : ((ForOfStatement)node).Initializer);
                GenerateNames((Node?)((IterationStatement)node).Statement);
                break;
            case SyntaxKind.SwitchStatement:
                GenerateNames(((SwitchStatement)node).CaseBlock);
                break;
            case SyntaxKind.CaseBlock:
                Core.ForEach(((CaseBlock)node).Clauses, (Node clause, int _) =>
                {
                    GenerateNames(clause);
                    return (object?)null;
                });
                break;
            case SyntaxKind.CaseClause:
            case SyntaxKind.DefaultClause:
                Core.ForEach(node is CaseClause caseClause ? caseClause.Statements : ((DefaultClause)node).Statements, (IStatement statement, int _) =>
                {
                    GenerateNames((Node)statement);
                    return (object?)null;
                });
                break;
            case SyntaxKind.TryStatement:
                GenerateNames(((TryStatement)node).TryBlock);
                GenerateNames(((TryStatement)node).CatchClause);
                GenerateNames(((TryStatement)node).FinallyBlock);
                break;
            case SyntaxKind.CatchClause:
                GenerateNames(((CatchClause)node).VariableDeclaration);
                GenerateNames(((CatchClause)node).Block);
                break;
            case SyntaxKind.VariableStatement:
                GenerateNames(((VariableStatement)node).DeclarationList);
                break;
            case SyntaxKind.VariableDeclarationList:
                Core.ForEach(((VariableDeclarationList)node).Declarations, (VariableDeclaration declaration, int _) =>
                {
                    GenerateNames(declaration);
                    return (object?)null;
                });
                break;
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
            case SyntaxKind.ClassDeclaration:
                GenerateNameIfNeeded(node is VariableDeclaration variableDeclaration ? variableDeclaration.Name
                    : node is ParameterDeclaration parameterDeclaration ? parameterDeclaration.Name
                    : node is BindingElement bindingElement ? bindingElement.Name
                    : ((ClassDeclaration)node).Name);
                break;
            case SyntaxKind.FunctionDeclaration:
                GenerateNameIfNeeded(((FunctionDeclaration)node).Name);
                if ((Utilities.GetEmitFlags(node) & EmitFlags.ReuseTempVariableScope) != 0)
                {
                    Core.ForEach(((FunctionDeclaration)node).Parameters, (ParameterDeclaration parameter, int _) =>
                    {
                        GenerateNames(parameter);
                        return (object?)null;
                    });
                    GenerateNames(((FunctionDeclaration)node).Body);
                }
                break;
            case SyntaxKind.ObjectBindingPattern:
                Core.ForEach(((ObjectBindingPattern)node).Elements, (BindingElement element, int _) =>
                {
                    GenerateNames(element);
                    return (object?)null;
                });
                break;
            case SyntaxKind.ArrayBindingPattern:
                Core.ForEach(((ArrayBindingPattern)node).Elements, (Node element, int _) =>
                {
                    GenerateNames(element);
                    return (object?)null;
                });
                break;
            case SyntaxKind.ImportDeclaration:
                GenerateNames(((ImportDeclaration)node).ImportClause);
                break;
            case SyntaxKind.ImportClause:
                GenerateNameIfNeeded(((ImportClause)node).Name);
                GenerateNames(((ImportClause)node).NamedBindings);
                break;
            case SyntaxKind.NamespaceImport:
                GenerateNameIfNeeded(((NamespaceImport)node).Name);
                break;
            case SyntaxKind.NamespaceExport:
                GenerateNameIfNeeded(((NamespaceExport)node).Name);
                break;
            case SyntaxKind.NamedImports:
                Core.ForEach(((NamedImports)node).Elements, (ImportSpecifier specifier, int _) =>
                {
                    GenerateNames(specifier);
                    return (object?)null;
                });
                break;
            case SyntaxKind.ImportSpecifier:
                GenerateNameIfNeeded(((ImportSpecifier)node).PropertyName ?? ((ImportSpecifier)node).Name);
                break;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5426-5440 — <c>generateMemberNames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference casts to <c>NamedDeclaration</c>, a union with no C# name; every kind this arm lists
    /// implements the port's <c>INamedDeclaration</c> capability interface, which is that union's <c>name</c> slot.
    /// </remarks>
    private void GenerateMemberNames(Node? node)
    {
        if (node is null) return;
        switch (node.Kind)
        {
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                GenerateNameIfNeeded(((INamedDeclaration)node).Name);
                break;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5442-5453 — <c>generateNameIfNeeded</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>DeclarationName</c> (<c>Identifier | PrivateIdentifier | StringLiteral |
    /// NumericLiteral | BigIntLiteral | ComputedPropertyName | BindingPattern</c>), which has no C# name; the body's three
    /// tests are all node tests and all return <c>Node</c>-takers, so the port takes the <c>Node</c> the union falls back
    /// to.
    /// </remarks>
    private void GenerateNameIfNeeded(Node? name)
    {
        if (name is not null)
        {
            if (Utilities.IsGeneratedIdentifier(name) || Utilities.IsGeneratedPrivateIdentifier(name))
            {
                GenerateName(name);
            }
            else if (Utilities.IsBindingPattern(name))
            {
                GenerateNames(name);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5456-5479 — the generated-name caches
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5456-5469 — <c>generateName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>GeneratedIdentifier | GeneratedPrivateIdentifier</c>; both are a
    /// <c>Node</c> wearing an auto-generate record (<c>EmitNodes.GetIdentifierAutoGenerate</c>), which is how the port
    /// reads it. The reference's `autoGeneratedIdToGeneratedName[id] || (autoGeneratedIdToGeneratedName[id] = …)` is a
    /// sparse-array slot: an index the array has not reached reads <c>undefined</c>, a falsy (empty) slot recomputes, and
    /// the write extends the array. C# arrays are dense and their indexer throws, so the read is guarded and the write
    /// resizes — the same two answers (<c>Checker.Check.Expressions.cs</c> uses the same shape for <c>flowTypeCache</c>).
    /// The local function keeps the reference's single expression, which assigns the slot only when it must.
    /// </remarks>
    private string GenerateName(Node name)
    {
        var autoGenerate = EmitNodes.GetIdentifierAutoGenerate(name)!;
        if ((autoGenerate.Flags & GeneratedIdentifierFlags.KindMask) == GeneratedIdentifierFlags.Node)
        {
            // Node names generate unique names based on their original node
            // and are cached based on that node's id.
            return GenerateNameCached(FactoryUtilities.GetNodeForGeneratedName(name), NodeTests.IsPrivateIdentifier(name), autoGenerate.Flags, autoGenerate.Prefix, autoGenerate.Suffix);
        }

        // Auto, Loop, and Unique names are cached based on their unique autoGenerateId.
        var autoGenerateId = autoGenerate.Id;
        return ReadOrMake(autoGenerateId);

        string ReadOrMake(int id)
        {
            var cached = id < _autoGeneratedIdToGeneratedName.Length ? _autoGeneratedIdToGeneratedName[id] : null;
            if (Js.IsTruthy(cached))
            {
                return cached;
            }

            var made = MakeName(name);
            if (id >= _autoGeneratedIdToGeneratedName.Length) Array.Resize(ref _autoGeneratedIdToGeneratedName, id + 1);
            _autoGeneratedIdToGeneratedName[id] = made;
            return made;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5471-5479 — <c>generateNameCached</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>cache</c> local is one of two sparse arrays and the write goes through that local,
    /// so the port cannot either: <c>Array.Resize</c> takes the array *by reference* and a local copy would leave the
    /// field pointing at the old (too small) instance. The local function below is the reference's
    /// `cache[nodeId] || (cache[nodeId] = …)` with the cache passed by <c>ref</c>, and the two calls are the reference's
    /// <c>privateName ? nodeIdToGeneratedPrivateName : nodeIdToGeneratedName</c> written out. The reference's optional
    /// <c>flags</c>/<c>prefix</c>/<c>suffix</c> are C# optional parameters; its union type for <c>prefix</c>
    /// (<c>string | GeneratedNamePart</c>) is <c>object?</c> here, which is what <c>formatGeneratedNamePart</c> takes.
    /// </remarks>
    private string GenerateNameCached(Node node, bool privateName, GeneratedIdentifierFlags? flags = null, object? prefix = null, string? suffix = null)
    {
        var nodeId = Utilities.GetNodeId(node);
        return privateName
            ? ReadOrMake(nodeId, ref _nodeIdToGeneratedPrivateName)
            : ReadOrMake(nodeId, ref _nodeIdToGeneratedName);

        string ReadOrMake(int id, ref string[] cache)
        {
            var cached = id < cache.Length ? cache[id] : null;
            if (Js.IsTruthy(cached))
            {
                return cached;
            }

            var generated = GenerateNameForNode(node, privateName, flags ?? GeneratedIdentifierFlags.None, FactoryUtilities.FormatGeneratedNamePart(prefix, GenerateName), FactoryUtilities.FormatGeneratedNamePart(suffix));
            if (id >= cache.Length) Array.Resize(ref cache, id + 1);
            cache[id] = generated;
            return generated;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5481-5537 — uniqueness
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5481-5485 — <c>isUniqueName</c>.</summary>
    private bool IsUniqueName(string name, bool privateName)
        => IsFileLevelUniqueNameInCurrentFile(name, privateName)
            && !IsReservedName(name, privateName)
            && !_generatedNames.Contains(name);

    /// <summary>ported from: src/compiler/emitter.ts:5487-5518 — <c>isReservedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>stack</c> local is one of two JS arrays and it walks them from the end backwards;
    /// the port's two stacks have the same type, and <c>Stack&lt;T&gt;</c>'s enumeration is already last-pushed-first
    /// (the reference's <c>i</c> from <c>length - 1</c> down to <c>0</c>), so a <c>foreach</c> over the chosen stack is
    /// the same walk. <c>set === stack[i]</c> is <c>ReferenceEquals</c>.
    /// </remarks>
    private bool IsReservedName(string name, bool privateName)
    {
        HashSet<string>? set;
        Stack<HashSet<string>?> stack;
        if (privateName)
        {
            set = _reservedPrivateNames;
            stack = _reservedPrivateNamesStack;
        }
        else
        {
            set = _reservedNames;
            stack = _reservedNamesStack;
        }

        if (set is not null && set.Contains(name))
        {
            return true;
        }
        foreach (var stackSet in stack)
        {
            if (ReferenceEquals(set, stackSet))
            {
                continue;
            }
            set = stackSet;
            if (set is not null && set.Contains(name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5520-5525 — <c>isFileLevelUniqueNameInCurrentFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>_isPrivate</c> parameter exists only to keep v8 from building an adaptor frame when
    /// the function is handed to <c>makeUniqueName</c>; it stays for the same reason. <c>isFileLevelUniqueName</c> is a
    /// <c>utilities.ts</c> reader whose third argument is the reference's <c>hasGlobalName</c> handler; the port's
    /// <c>IPrintHandlers.HasGlobalName</c> is that handler, and <c>Utilities.IsFileLevelUniqueName</c> takes it as the
    /// same callback.
    /// </remarks>
    private bool IsFileLevelUniqueNameInCurrentFile(string name, bool _isPrivate)
        => _currentSourceFile is not null ? Utilities.IsFileLevelUniqueName(_currentSourceFile, name, _handlers.HasGlobalName) : true;

    /// <summary>ported from: src/compiler/emitter.ts:5527-5537 — <c>isUniqueLocalName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>container</c> is the union <c>HasLocals | undefined</c>, which has no C# name (§15.9); the two
    /// slots the walk reads (<c>locals</c>, <c>nextContainer</c>) are the port's <c>ILocalsContainer</c> capability
    /// interface, so the parameter stays the <c>Node?</c> every <c>HasLocals</c> member also is and the walk narrows
    /// through that interface. The reference's <c>table.get(name)</c> is a JS Map lookup that may miss, which the port
    /// writes as <c>GetValueOrDefault</c> (Model/SymbolTable.cs).
    /// </remarks>
    private bool IsUniqueLocalName(string name, Node? container)
    {
        for (var node = container; node is not null && Utilities.IsNodeDescendantOf(node, container); node = (node as ILocalsContainer)?.NextContainer)
        {
            if (node is ILocalsContainer localsContainer && Utilities.CanHaveLocals(node) && localsContainer.Locals is not null)
            {
                var local = localsContainer.Locals.GetValueOrDefault(Utilities.EscapeLeadingUnderscores(name));
                // We conservatively include alias symbols to cover cases where they're emitted as locals
                if (local is not null && (local.Flags & (SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias)) != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5540-5568 — the temp-flag counters
    // ---------------------------------------------------------------------------------------------

    /// <remarks>
    /// PORT-NOTE: the reference's <c>TempFlags</c> (emitter.ts:6345-6349) is <c>Auto = 0</c>,
    /// <c>CountMask = 0x0FFFFFFF</c>, <c>_i = 0x10000000</c>; the port's <c>TempFlags</c> — the cross-slice field contract
    /// in <c>Printer.State.cs</c>, which this file must not edit — declares neither of the latter two (its
    /// <c>CountMask</c> is the 4-bit value). The name generator below is their only reader, so the two reference values
    /// live here under their own names; the integration owner folds them into the enum and deletes these two constants.
    /// <c>TempFlags.Auto</c> is the reference's own <c>0</c> and is used as-is.
    /// </remarks>
    private const TempFlags TempFlagsI = (TempFlags)0x10000000;

    /// <inheritdoc cref="TempFlagsI"/>
    private const TempFlags TempFlagsCountMask = (TempFlags)0x0FFFFFFF;

    /// <summary>ported from: src/compiler/emitter.ts:5540-5549 — <c>getTempFlags</c>.</summary>
    private TempFlags GetTempFlags(string formattedNameKey)
    {
        switch (formattedNameKey)
        {
            case "":
                return _tempFlags;
            case "#":
                return _privateNameTempFlags;
            default:
                return _formattedNameTempFlags is not null && _formattedNameTempFlags.TryGetValue(formattedNameKey, out var flags)
                    ? flags
                    : TempFlags.Auto;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5551-5566 — <c>setTempFlags</c>.</summary>
    private void SetTempFlags(string formattedNameKey, TempFlags flags)
    {
        switch (formattedNameKey)
        {
            case "":
                _tempFlags = flags;
                break;
            case "#":
                _privateNameTempFlags = flags;
                break;
            default:
                _formattedNameTempFlags ??= [];
                _formattedNameTempFlags[formattedNameKey] = flags;
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // emitter.ts:5571-5676 — making the names
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5571-5624 — <c>makeTempVariableName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference increments its numeric flag with <c>tempFlags++</c>; C# has no increment on an enum, so
    /// the loop writes <c>(TempFlags)((int)tempFlags + 1)</c> — the same add-one. <c>count !== 8 &amp;&amp; count !== 13</c>
    /// is the reference's own "skip over 'i' and 'n'", and <c>String.fromCharCode(CharacterCodes.a + count)</c> is the
    /// <c>(char)</c> cast of the same code unit.
    /// </remarks>
    private string MakeTempVariableName(TempFlags flags, bool reservedInNestedScopes, bool privateName, string prefix, string suffix)
    {
        if (prefix.Length > 0 && prefix[0] == CharacterCodes.Hash)
        {
            prefix = prefix[1..];
        }

        // Generate a key to use to acquire a TempFlags counter based on the fixed portions of the generated name.
        var key = FactoryUtilities.FormatGeneratedName(privateName, prefix, "", suffix);
        var tempFlags = GetTempFlags(key);

        if (flags != TempFlags.Auto && (tempFlags & flags) == TempFlags.Auto)
        {
            var name = flags == TempFlagsI ? "_i" : "_n";
            var fullName = FactoryUtilities.FormatGeneratedName(privateName, prefix, name, suffix);
            if (IsUniqueName(fullName, privateName))
            {
                tempFlags |= flags;
                if (privateName)
                {
                    ReservePrivateNameInNestedScopes(fullName);
                }
                else if (reservedInNestedScopes)
                {
                    ReserveNameInNestedScopes(fullName);
                }
                SetTempFlags(key, tempFlags);
                return fullName;
            }
        }

        while (true)
        {
            var count = (int)(tempFlags & TempFlagsCountMask);
            tempFlags = (TempFlags)((int)tempFlags + 1);
            // Skip over 'i' and 'n'
            if (count != 8 && count != 13)
            {
                var name = count < 26
                    ? "_" + (char)(CharacterCodes.a + count)
                    : "_" + (count - 26);
                var fullName = FactoryUtilities.FormatGeneratedName(privateName, prefix, name, suffix);
                if (IsUniqueName(fullName, privateName))
                {
                    if (privateName)
                    {
                        ReservePrivateNameInNestedScopes(fullName);
                    }
                    else if (reservedInNestedScopes)
                    {
                        ReserveNameInNestedScopes(fullName);
                    }
                    SetTempFlags(key, tempFlags);
                    return fullName;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5626-5669 — <c>makeUniqueName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference defaults <c>checkFn</c> to <c>isUniqueName</c>; a C# default argument cannot name an
    /// instance method, so the parameter is optional and the fallback is applied first — the same callee for every call
    /// site that omits it. The reference's <c>charCodeAt(baseName.length - 1) !== CharacterCodes._</c> reads <c>NaN</c> for
    /// an empty base name, which is not the underscore, so the port guards the empty string and appends the same suffix.
    /// </remarks>
    private string MakeUniqueName(
        string baseName,
        Func<string, bool, bool>? checkFn = null,
        bool optimistic = false,
        bool scoped = false,
        bool privateName = false,
        string prefix = "",
        string suffix = "")
    {
        checkFn ??= IsUniqueName;
        if (baseName.Length > 0 && baseName[0] == CharacterCodes.Hash)
        {
            baseName = baseName[1..];
        }
        if (prefix.Length > 0 && prefix[0] == CharacterCodes.Hash)
        {
            prefix = prefix[1..];
        }
        if (optimistic)
        {
            var fullName = FactoryUtilities.FormatGeneratedName(privateName, prefix, baseName, suffix);
            if (checkFn(fullName, privateName))
            {
                if (privateName)
                {
                    ReservePrivateNameInNestedScopes(fullName);
                }
                else if (scoped)
                {
                    ReserveNameInNestedScopes(fullName);
                }
                else
                {
                    _generatedNames.Add(fullName);
                }
                return fullName;
            }
        }
        // Find the first unique 'name_n', where n is a positive number
        if (baseName.Length == 0 || baseName[^1] != CharacterCodes.Underscore)
        {
            baseName += "_";
        }
        var i = 1;
        while (true)
        {
            var fullName = FactoryUtilities.FormatGeneratedName(privateName, prefix, baseName + i, suffix);
            if (checkFn(fullName, privateName))
            {
                if (privateName)
                {
                    ReservePrivateNameInNestedScopes(fullName);
                }
                else if (scoped)
                {
                    ReserveNameInNestedScopes(fullName);
                }
                else
                {
                    _generatedNames.Add(fullName);
                }
                return fullName;
            }
            i++;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5671-5673 — <c>makeFileLevelOptimisticUniqueName</c>.</summary>
    private string MakeFileLevelOptimisticUniqueName(string name)
        => MakeUniqueName(name, IsFileLevelUniqueNameInCurrentFile, /*optimistic*/ true, /*scoped*/ false, /*privateName*/ false, /*prefix*/ "", /*suffix*/ "");

    /// <summary>ported from: src/compiler/emitter.ts:5678-5682 — <c>generateNameForModuleOrEnum</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ModuleDeclaration | EnumDeclaration</c>, which has no C# name; the body
    /// reads only <c>name</c>, through the same <c>INamedDeclaration</c> the other union-parameter methods here use, and
    /// <c>tryCast(node, canHaveLocals)</c> answers the <c>HasLocals | undefined</c> the port expresses with
    /// <c>Core.TryCast</c> over the same predicate.
    /// </remarks>
    private string GenerateNameForModuleOrEnum(Node node)
    {
        var name = GetTextOfNode(((INamedDeclaration)node).Name!);
        // Use module/enum name itself if it is unique, otherwise make a unique variation
        return IsUniqueLocalName(name, Core.TryCast<Node, Node>(node, Utilities.CanHaveLocals))
            ? name
            : MakeUniqueName(name, IsUniqueName, /*optimistic*/ false, /*scoped*/ false, /*privateName*/ false, /*prefix*/ "", /*suffix*/ "");
    }

    /// <summary>ported from: src/compiler/emitter.ts:5687-5693 — <c>generateNameForImportOrExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ImportDeclaration | ExportDeclaration</c>, which has no C# name; the
    /// body's first read is <c>getExternalModuleName</c>, which the port already states over <c>Node?</c>
    /// (<c>Utilities.GetExternalModuleName</c>, utilities.ts:4407).
    /// </remarks>
    private string GenerateNameForImportOrExportDeclaration(Node node)
    {
        var expr = Utilities.GetExternalModuleName(node)!; // TODO: GH#18217
        var baseName = NodeTests.IsStringLiteral(expr)
            ? Utilities.MakeIdentifierFromModuleName(((StringLiteral)expr).Text!)
            : "module";
        return MakeUniqueName(baseName, IsUniqueName, /*optimistic*/ false, /*scoped*/ false, /*privateName*/ false, /*prefix*/ "", /*suffix*/ "");
    }

    /// <summary>ported from: src/compiler/emitter.ts:5697-5699 — <c>generateNameForExportDefault</c>.</summary>
    private string GenerateNameForExportDefault()
        => MakeUniqueName("default", IsUniqueName, /*optimistic*/ false, /*scoped*/ false, /*privateName*/ false, /*prefix*/ "", /*suffix*/ "");

    /// <summary>ported from: src/compiler/emitter.ts:5704-5706 — <c>generateNameForClassExpression</c>.</summary>
    private string GenerateNameForClassExpression()
        => MakeUniqueName("class", IsUniqueName, /*optimistic*/ false, /*scoped*/ false, /*privateName*/ false, /*prefix*/ "", /*suffix*/ "");

    /// <summary>ported from: src/compiler/emitter.ts:5708-5713 — <c>generateNameForMethodOrAccessor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>MethodDeclaration | AccessorDeclaration</c>, which has no C# name; the
    /// body reads only <c>name</c>, and <c>isIdentifier(node.name)</c> answers false for the undefined a missing name
    /// gives, so the port tests for a present name first.
    /// </remarks>
    private string GenerateNameForMethodOrAccessor(Node node, bool privateName, string prefix, string suffix)
    {
        var name = ((INamedDeclaration)node).Name;
        if (name is not null && NodeTests.IsIdentifier(name))
        {
            return GenerateNameCached(name, privateName);
        }
        return MakeTempVariableName(TempFlags.Auto, /*reservedInNestedScopes*/ false, privateName, prefix, suffix);
    }

    /// <summary>ported from: src/compiler/emitter.ts:5718-5766 — <c>generateNameForNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference casts its branches to the members of the same unions as above; the two arms that read a
    /// name (<c>FunctionDeclaration | ClassDeclaration</c>) read it through <c>INamedDeclaration</c>, and the
    /// <c>Debug.assert(!prefix &amp;&amp; !suffix &amp;&amp; !privateName)</c> guards become the port's string truthiness
    /// (<c>Js.IsTruthy</c>) because the reference's <c>!</c> on a string is JS falsiness, not "is null".
    /// </remarks>
    private string GenerateNameForNode(Node node, bool privateName, GeneratedIdentifierFlags flags, string prefix, string suffix)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                return MakeUniqueName(
                    GetTextOfNode(node),
                    IsUniqueName,
                    (flags & GeneratedIdentifierFlags.Optimistic) != 0,
                    (flags & GeneratedIdentifierFlags.ReservedInNestedScopes) != 0,
                    privateName,
                    prefix,
                    suffix);
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.EnumDeclaration:
                Debug.Assert(!Js.IsTruthy(prefix) && !Js.IsTruthy(suffix) && !privateName);
                return GenerateNameForModuleOrEnum(node);
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
                Debug.Assert(!Js.IsTruthy(prefix) && !Js.IsTruthy(suffix) && !privateName);
                return GenerateNameForImportOrExportDeclaration(node);
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ClassDeclaration:
                {
                    Debug.Assert(!Js.IsTruthy(prefix) && !Js.IsTruthy(suffix) && !privateName);
                    var name = ((INamedDeclaration)node).Name;
                    if (name is not null && !Utilities.IsGeneratedIdentifier(name))
                    {
                        return GenerateNameForNode(name, /*privateName*/ false, flags, prefix, suffix);
                    }
                    return GenerateNameForExportDefault();
                }
            case SyntaxKind.ExportAssignment:
                Debug.Assert(!Js.IsTruthy(prefix) && !Js.IsTruthy(suffix) && !privateName);
                return GenerateNameForExportDefault();
            case SyntaxKind.ClassExpression:
                Debug.Assert(!Js.IsTruthy(prefix) && !Js.IsTruthy(suffix) && !privateName);
                return GenerateNameForClassExpression();
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return GenerateNameForMethodOrAccessor(node, privateName, prefix, suffix);
            case SyntaxKind.ComputedPropertyName:
                return MakeTempVariableName(TempFlags.Auto, /*reservedInNestedScopes*/ true, privateName, prefix, suffix);
            default:
                return MakeTempVariableName(TempFlags.Auto, /*reservedInNestedScopes*/ false, privateName, prefix, suffix);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5768-5793 — <c>makeName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>GeneratedIdentifier | GeneratedPrivateIdentifier</c>, which the port
    /// takes as the <c>Node</c> wearing the auto-generate record the body reads. The reference's
    /// <c>Debug.fail(`… ${Debug.formatEnum(…, (ts as any).GeneratedIdentifierFlags, true)}`)</c> format its flags through
    /// the enum's values, which the port's <c>Debug.FormatEnum</c> derives from the value it is given. The <c>Unique</c>
    /// arm's choice between the two check functions is bound to a local first, because a C# conditional of two method
    /// groups needs one target type.
    /// </remarks>
    private string MakeName(Node name)
    {
        var autoGenerate = EmitNodes.GetIdentifierAutoGenerate(name)!;
        var prefix = FactoryUtilities.FormatGeneratedNamePart(autoGenerate.Prefix, GenerateName);
        var suffix = FactoryUtilities.FormatGeneratedNamePart(autoGenerate.Suffix);
        switch (autoGenerate.Flags & GeneratedIdentifierFlags.KindMask)
        {
            case GeneratedIdentifierFlags.Auto:
                return MakeTempVariableName(TempFlags.Auto, (autoGenerate.Flags & GeneratedIdentifierFlags.ReservedInNestedScopes) != 0, NodeTests.IsPrivateIdentifier(name), prefix, suffix);
            case GeneratedIdentifierFlags.Loop:
                Debug.AssertNode(name, NodeTests.IsIdentifier);
                return MakeTempVariableName(TempFlagsI, (autoGenerate.Flags & GeneratedIdentifierFlags.ReservedInNestedScopes) != 0, /*privateName*/ false, prefix, suffix);
            case GeneratedIdentifierFlags.Unique:
                Func<string, bool, bool> checkFn = (autoGenerate.Flags & GeneratedIdentifierFlags.FileLevel) != 0
                    ? IsFileLevelUniqueNameInCurrentFile
                    : IsUniqueName;
                return MakeUniqueName(
                    Utilities.IdText(name),
                    checkFn,
                    (autoGenerate.Flags & GeneratedIdentifierFlags.Optimistic) != 0,
                    (autoGenerate.Flags & GeneratedIdentifierFlags.ReservedInNestedScopes) != 0,
                    NodeTests.IsPrivateIdentifier(name),
                    prefix,
                    suffix);
        }

        return Debug.Fail<string>($"Unsupported GeneratedIdentifierKind: {Debug.FormatEnum(autoGenerate.Flags & GeneratedIdentifierFlags.KindMask, isFlags: true)}.");
    }
}