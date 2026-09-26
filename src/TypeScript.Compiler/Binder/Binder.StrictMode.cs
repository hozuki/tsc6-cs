namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2558-2591 (checkContextualIdentifier)
// ported from: src/compiler/binder.ts:2593-2607 (getStrictModeIdentifierMessage)
// ported from: src/compiler/binder.ts:2609-2616 (checkPrivateIdentifier)
// ported from: src/compiler/binder.ts:2618-2624 (checkStrictModeBinaryExpression)
// ported from: src/compiler/binder.ts:2626-2632 (checkStrictModeCatchClause)
// ported from: src/compiler/binder.ts:2634-2642 (checkStrictModeDeleteExpression)
// ported from: src/compiler/binder.ts:2644-2646 (isEvalOrArgumentsIdentifier)
// ported from: src/compiler/binder.ts:2648-2658 (checkStrictModeEvalOrArguments)
// ported from: src/compiler/binder.ts:2660-2672 (getStrictModeEvalOrArgumentsMessage)
// ported from: src/compiler/binder.ts:2674-2679 (checkStrictModeFunctionName)
// ported from: src/compiler/binder.ts:2681-2693 (getStrictModeBlockScopeFunctionDeclarationMessage)
// ported from: src/compiler/binder.ts:2695-2709 (checkStrictModeFunctionDeclaration)
// ported from: src/compiler/binder.ts:2711-2719 (checkStrictModePostfixUnaryExpression)
// ported from: src/compiler/binder.ts:2721-2728 (checkStrictModePrefixUnaryExpression)
// ported from: src/compiler/binder.ts:2730-2735 (checkStrictModeWithStatement)
// ported from: src/compiler/binder.ts:2737-2744 (checkStrictModeLabeledStatement)
// ported from: src/compiler/binder.ts:2746-2749 (errorOnFirstToken)
/// <summary>
/// The reserved-word and strict-mode grammar checks the binder performs on the nodes it visits (its
/// <c>bindWorker</c> arms call in here) plus the diagnostic helper they share, <c>errorOnFirstToken</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: every member here is <c>internal</c> because the dispatch slice (M5-5) and the module slice (M5-4, whose
/// <c>bindModuleDeclaration</c> calls <c>errorOnFirstToken</c>) are their callers; the helpers only this file reads
/// are private. TS's messages are chosen the reference's way — by asking whether the node sits in a class, then
/// whether the file is a module — so <c>file.externalModuleIndicator</c> is the port's
/// <c>Js.IsTruthy(_file.ExternalModuleIndicator)</c> (§12.10's note on that slot's <c>object?</c> type).
/// <para>
/// PORT-NOTE: TS's <c>checkContextualIdentifier(node: Identifier)</c> is called with an erased cast from an arm the
/// <c>ThisKeyword</c> case shares, so a <c>ThisExpression</c> does reach it at run time; upstream it falls out
/// because <c>identifierToKeywordKind</c> misses on an absent <c>escapedText</c>. The port's
/// <see cref="Utilities.IdentifierToKeywordKind"/> and <see cref="Utilities.IsIdentifierName"/> take the concrete
/// <see cref="Identifier"/>, so this member takes the <see cref="Node"/> the reference really passes and narrows
/// inside — the same "no diagnostic" answer for every non-identifier, without depending on an erased cast.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>node.left as Identifier</c>/<c>node.operand as Identifier</c>/<c>node.left</c> casts are erased
/// before the callee's own <c>name.kind === SyntaxKind.Identifier</c> test, so the port passes the expression itself
/// and lets that test do the narrowing: a non-identifier is reported by neither.
/// </para>
/// <para>
/// PORT-NOTE: the diagnostics helpers are the reference's own names in this class —
/// <see cref="CreateDiagnosticForNode"/> (binder.ts:563-569) for the node-anchored ones and
/// <c>Utilities.CreateFileDiagnostic</c>/<c>Utilities.GetErrorSpanForNode</c>/<c>Utilities.GetSpanOfTokenAtPosition</c>
/// for the span-anchored ones — all already ported in Utilities/Utilities.Binder.Diagnostics.cs.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2558-2591 — <c>checkContextualIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>identifierToKeywordKind(node)</c> and returns on <c>undefined</c>; the port states that
    /// as the nullable answer of the same helper. TS's two range tests are on the future-reserved-word window of the
    /// keyword table, and its <c>node.flags &amp; NodeFlags.X</c> tests are the port's mask form.
    /// </remarks>
    internal void CheckContextualIdentifier(Node node)
    {
        // Report error only if there are no parse errors in file
        if (
            _file.ParseDiagnostics!.Count == 0 &&
            (node.Flags & NodeFlags.Ambient) == 0 &&
            (node.Flags & NodeFlags.JSDoc) == 0 &&
            node is Identifier identifier &&
            !Utilities.IsIdentifierName(identifier)
        )
        {
            // strict mode identifiers
            var originalKeywordKind = Utilities.IdentifierToKeywordKind(identifier);
            if (originalKeywordKind is null)
            {
                return;
            }

            var keywordKind = originalKeywordKind.Value;
            if (
                _inStrictMode &&
                keywordKind >= SyntaxKind.FirstFutureReservedWord &&
                keywordKind <= SyntaxKind.LastFutureReservedWord
            )
            {
                _file.BindDiagnostics!.Add(CreateDiagnosticForNode(identifier, GetStrictModeIdentifierMessage(identifier), Utilities.DeclarationNameToString(identifier)));
            }
            else if (keywordKind == SyntaxKind.AwaitKeyword)
            {
                if (Parser.IsExternalModule(_file) && Utilities.IsInTopLevelContext(identifier))
                {
                    _file.BindDiagnostics!.Add(CreateDiagnosticForNode(identifier, Diagnostics.Identifier_expected_0_is_a_reserved_word_at_the_top_level_of_a_module, Utilities.DeclarationNameToString(identifier)));
                }
                else if ((identifier.Flags & NodeFlags.AwaitContext) != 0)
                {
                    _file.BindDiagnostics!.Add(CreateDiagnosticForNode(identifier, Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, Utilities.DeclarationNameToString(identifier)));
                }
            }
            else if (keywordKind == SyntaxKind.YieldKeyword && (identifier.Flags & NodeFlags.YieldContext) != 0)
            {
                _file.BindDiagnostics!.Add(CreateDiagnosticForNode(identifier, Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, Utilities.DeclarationNameToString(identifier)));
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2593-2607 — <c>getStrictModeIdentifierMessage</c>.</summary>
    /// <remarks>
    /// Provide specialized messages to help the user understand why we think they're in strict mode.
    /// PORT-NOTE: TS's <c>file.externalModuleIndicator</c> is a truthiness test on a slot the port types <c>object?</c>,
    /// so it is <c>Js.IsTruthy</c> (§5's rule 1/§12.10); <c>getContainingClass</c> is
    /// <see cref="Utilities.GetContainingClass"/>.
    /// </remarks>
    private DiagnosticMessage GetStrictModeIdentifierMessage(Node node)
    {
        if (Utilities.GetContainingClass(node) is not null)
        {
            return Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode_Class_definitions_are_automatically_in_strict_mode;
        }

        if (Js.IsTruthy(_file.ExternalModuleIndicator))
        {
            return Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode_Modules_are_automatically_in_strict_mode;
        }

        return Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode;
    }

    /// <summary>ported from: src/compiler/binder.ts:2609-2616 — <c>checkPrivateIdentifier</c>.</summary>
    /// <remarks>
    /// The binder visits every node, so this is a good place to check for the reserved private name (there is only one).
    /// </remarks>
    internal void CheckPrivateIdentifier(PrivateIdentifier node)
    {
        if (node.EscapedText == "#constructor")
        {
            // Report error only if there are no parse errors in file
            if (_file.ParseDiagnostics!.Count == 0)
            {
                _file.BindDiagnostics!.Add(CreateDiagnosticForNode(node, Diagnostics.constructor_is_a_reserved_word, Utilities.DeclarationNameToString(node)));
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2618-2624 — <c>checkStrictModeBinaryExpression</c>.</summary>
    internal void CheckStrictModeBinaryExpression(BinaryExpression node)
    {
        if (_inStrictMode && Utilities.IsLeftHandSideExpression(node.Left!) && Utilities.IsAssignmentOperator(node.OperatorToken!.Kind))
        {
            // ECMA 262 (Annex C) The identifier eval or arguments may not appear as the LeftHandSideExpression of an
            // Assignment operator(11.13) or of a PostfixExpression(11.3)
            CheckStrictModeEvalOrArguments(node, node.Left);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2626-2632 — <c>checkStrictModeCatchClause</c>.</summary>
    internal void CheckStrictModeCatchClause(CatchClause node)
    {
        // It is a SyntaxError if a TryStatement with a Catch occurs within strict code and the Identifier of the
        // Catch production is eval or arguments
        if (_inStrictMode && node.VariableDeclaration is not null)
        {
            CheckStrictModeEvalOrArguments(node, node.VariableDeclaration.Name);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2634-2642 — <c>checkStrictModeDeleteExpression</c>.</summary>
    internal void CheckStrictModeDeleteExpression(DeleteExpression node)
    {
        // Grammar checking
        var expression = node.Expression;
        if (_inStrictMode && expression is not null && expression.Kind == SyntaxKind.Identifier)
        {
            // When a delete operator occurs within strict mode code, a SyntaxError is thrown if its
            // UnaryExpression is a direct reference to a variable, function argument, or function name
            var span = Utilities.GetErrorSpanForNode(_file, expression);
            _file.BindDiagnostics!.Add(Utilities.CreateFileDiagnostic(_file, span.Start, span.Length, Diagnostics.delete_cannot_be_called_on_an_identifier_in_strict_mode));
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2644-2646 — <c>isEvalOrArgumentsIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard is <c>isIdentifier(node)</c> plus two <c>escapedText</c> comparisons; the port states the
    /// narrowing as an <c>is</c> pattern, which is the same test.
    /// </remarks>
    private static bool IsEvalOrArgumentsIdentifier(Node node)
        => node is Identifier identifier && (identifier.EscapedText == "eval" || identifier.EscapedText == "arguments");

    /// <summary>ported from: src/compiler/binder.ts:2648-2658 — <c>checkStrictModeEvalOrArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>name &amp;&amp; name.kind === SyntaxKind.Identifier</c> is the port's null test plus the same
    /// kind test; the cast to <see cref="Identifier"/> is the kind test's license. <c>idText(identifier)</c> is
    /// <see cref="Utilities.IdText"/>.
    /// </remarks>
    internal void CheckStrictModeEvalOrArguments(Node contextNode, Node? name)
    {
        if (name is not null && name.Kind == SyntaxKind.Identifier)
        {
            var identifier = (Identifier)name;
            if (IsEvalOrArgumentsIdentifier(identifier))
            {
                // We check first if the name is inside class declaration or class expression; if so give explicit message
                // otherwise report generic error message.
                var span = Utilities.GetErrorSpanForNode(_file, name);
                _file.BindDiagnostics!.Add(Utilities.CreateFileDiagnostic(_file, span.Start, span.Length, GetStrictModeEvalOrArgumentsMessage(contextNode), Utilities.IdText(identifier)));
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2660-2672 — <c>getStrictModeEvalOrArgumentsMessage</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as in <c>getStrictModeIdentifierMessage</c> — <c>getContainingClass</c> via
    /// <see cref="Utilities.GetContainingClass"/> and the module test as <c>Js.IsTruthy</c> on the
    /// <c>object?</c> slot.
    /// </remarks>
    private DiagnosticMessage GetStrictModeEvalOrArgumentsMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in strict mode.
        if (Utilities.GetContainingClass(node) is not null)
        {
            return Diagnostics.Code_contained_in_a_class_is_evaluated_in_JavaScript_s_strict_mode_which_does_not_allow_this_use_of_0_For_more_information_see_https_Colon_Slash_Slashdeveloper_mozilla_org_Slashen_US_Slashdocs_SlashWeb_SlashJavaScript_SlashReference_SlashStrict_mode;
        }

        if (Js.IsTruthy(_file.ExternalModuleIndicator))
        {
            return Diagnostics.Invalid_use_of_0_Modules_are_automatically_in_strict_mode;
        }

        return Diagnostics.Invalid_use_of_0_in_strict_mode;
    }

    /// <summary>ported from: src/compiler/binder.ts:2674-2679 — <c>checkStrictModeFunctionName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>FunctionLikeDeclaration</c> union and its one read is <c>node.name</c>;
    /// the port's <see cref="IFunctionLikeDeclarationBase"/> reaches that slot through
    /// <see cref="INamedDeclaration"/>, which every function-like node implements. The check's context parameter is
    /// the concrete <see cref="Node"/> TS's union satisfies, so the interface is cast to it (an interface is not
    /// implicitly convertible to a class), and the cast holds for the two call sites — a function declaration or
    /// expression.
    /// </remarks>
    internal void CheckStrictModeFunctionName(IFunctionLikeDeclarationBase node)
    {
        if (_inStrictMode && (node.Flags & NodeFlags.Ambient) == 0)
        {
            // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a
            // strict mode FunctionDeclaration or FunctionExpression (13.1))
            CheckStrictModeEvalOrArguments((Node)node, node.Name);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2681-2693 — <c>getStrictModeBlockScopeFunctionDeclarationMessage</c>.</summary>
    private DiagnosticMessage GetStrictModeBlockScopeFunctionDeclarationMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in strict mode.
        if (Utilities.GetContainingClass(node) is not null)
        {
            return Diagnostics.Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5_Class_definitions_are_automatically_in_strict_mode;
        }

        if (Js.IsTruthy(_file.ExternalModuleIndicator))
        {
            return Diagnostics.Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5_Modules_are_automatically_in_strict_mode;
        }

        return Diagnostics.Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5;
    }

    /// <summary>ported from: src/compiler/binder.ts:2695-2709 — <c>checkStrictModeFunctionDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the three container tests read <c>blockScopeContainer</c>'s kind and the
    /// function-like/static-block predicate, which is <see cref="Utilities.IsFunctionLikeOrClassStaticBlockDeclaration"/>.
    /// </remarks>
    internal void CheckStrictModeFunctionDeclaration(FunctionDeclaration node)
    {
        if (_languageVersion < ScriptTarget.ES2015)
        {
            var blockScopeContainer = _blockScopeContainer!;
            // Report error if function is not top level function declaration
            if (
                blockScopeContainer.Kind != SyntaxKind.SourceFile &&
                blockScopeContainer.Kind != SyntaxKind.ModuleDeclaration &&
                !Utilities.IsFunctionLikeOrClassStaticBlockDeclaration(blockScopeContainer)
            )
            {
                // We check first if the name is inside class declaration or class expression; if so give explicit message
                // otherwise report generic error message.
                var errorSpan = Utilities.GetErrorSpanForNode(_file, node);
                _file.BindDiagnostics!.Add(Utilities.CreateFileDiagnostic(_file, errorSpan.Start, errorSpan.Length, GetStrictModeBlockScopeFunctionDeclarationMessage(node)));
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2711-2719 — <c>checkStrictModePostfixUnaryExpression</c>.</summary>
    internal void CheckStrictModePostfixUnaryExpression(PostfixUnaryExpression node)
    {
        // Grammar checking
        // The identifier eval or arguments may not appear as the LeftHandSideExpression of an
        // Assignment operator(11.13) or of a PostfixExpression(11.3) or as the UnaryExpression
        // operated upon by a Prefix Increment(11.4.4) or a Prefix Decrement(11.4.5) operator.
        if (_inStrictMode)
        {
            CheckStrictModeEvalOrArguments(node, node.Operand);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2721-2728 — <c>checkStrictModePrefixUnaryExpression</c>.</summary>
    internal void CheckStrictModePrefixUnaryExpression(PrefixUnaryExpression node)
    {
        // Grammar checking
        if (_inStrictMode)
        {
            if (node.Operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken)
            {
                CheckStrictModeEvalOrArguments(node, node.Operand);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2730-2735 — <c>checkStrictModeWithStatement</c>.</summary>
    internal void CheckStrictModeWithStatement(WithStatement node)
    {
        // Grammar checking for withStatement
        if (_inStrictMode)
        {
            ErrorOnFirstToken(node, Diagnostics.with_statements_are_not_allowed_in_strict_mode);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2737-2744 — <c>checkStrictModeLabeledStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two statement tests read <c>node.statement</c>; the port's slot is <c>IStatement?</c>, so the
    /// cast to <see cref="Node"/> is what the two <c>Node</c>-taking predicates need. <c>getEmitScriptTarget(options)</c>
    /// is <see cref="Utilities.GetEmitScriptTarget"/> over the binder's nullable options slot.
    /// </remarks>
    internal void CheckStrictModeLabeledStatement(LabeledStatement node)
    {
        // Grammar checking for labeledStatement
        if (_inStrictMode && Utilities.GetEmitScriptTarget(_options) >= ScriptTarget.ES2015)
        {
            var statement = (Node)node.Statement!;
            if (Utilities.IsDeclarationStatement(statement) || NodeTests.IsVariableStatement(statement))
            {
                ErrorOnFirstToken(node.Label!, Diagnostics.A_label_is_not_allowed_here);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2746-2749 — <c>errorOnFirstToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>...args: DiagnosticArguments</c> spread is the port's <c>params object?[]</c>, passed
    /// through to <c>createFileDiagnostic</c> as-is; the caller at binder.ts:2367 (M5-4's <c>bindModuleDeclaration</c>)
    /// is why this member is <c>internal</c>.
    /// </remarks>
    internal void ErrorOnFirstToken(Node node, DiagnosticMessage message, params object?[] args)
    {
        var span = Utilities.GetSpanOfTokenAtPosition(_file, node.Pos);
        _file.BindDiagnostics!.Add(Utilities.CreateFileDiagnostic(_file, span.Start, span.Length, message, args));
    }
}
