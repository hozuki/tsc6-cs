#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/ts.ts:2607-2750 — the emit-notification and substitution hooks of
// `transformTypeScript`'s body.
/// <summary>
/// The substitution half of the TypeScript erase transform (src/compiler/transformers/ts.ts:2607-2750):
/// <c>onEmitNode</c>/<c>onSubstituteNode</c> and the two substitutions they perform — a namespace-exported name's
/// <c>container.x</c> qualification, and a const enum member's inlined value.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the hooks' types): TS's <c>onEmitNode(hint, node, emitCallback)</c> is the context's
/// <c>EmitNotification</c> and <c>onSubstituteNode(hint, node)</c> its <c>EmitSubstitution</c>; the port's slots are
/// <see cref="Action{T1,T2,T3}"/>/<see cref="Func{T1,T2,TResult}"/> over <see cref="EmitHint"/> and
/// <see cref="Node"/> (Transformer.cs's <see cref="ITransformerContext"/>), and the saved previous hooks are the
/// fields <c>_previousOnEmitNode</c>/<c>_previousOnSubstituteNode</c>.
/// </para>
/// <para>
/// PORT-NOTE (the constant-value domain): <c>getConstantValue</c> answers <c>string | number | undefined</c>; the port's
/// <c>object?</c> holds the same two runtime values (a <c>string</c> or a <c>double</c>), which is why the two numeric
/// arms test the type before comparing it against zero.
/// </para>
/// <para>
/// PORT-NOTE (<c>safeMultiLineComment</c>): <c>value.replace(/\*\//g, "*_/")</c> is a compile-time-constant pattern
/// with a literal replacement, so the port states the global <see cref="string.Replace(string,string)"/> (the same
/// treatment <c>Core/Paths.cs</c> and <c>Emitter.cs</c> give their literal patterns).
/// </para>
/// </remarks>
internal sealed partial class TypeScriptTransformer
{
    /// <summary>ported from: src/compiler/transformers/ts.ts:2613-2639 — <c>onEmitNode</c>.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        var savedApplicableSubstitutions = _applicableSubstitutions;
        var savedCurrentSourceFile = _currentSourceFile;

        if (NodeTests.IsSourceFile(node))
        {
            _currentSourceFile = (SourceFile)node;
        }

        if ((_enabledSubstitutions & TypeScriptSubstitutionFlags.NamespaceExports) != 0 && IsTransformedModuleDeclaration(node))
        {
            _applicableSubstitutions |= TypeScriptSubstitutionFlags.NamespaceExports;
        }

        if ((_enabledSubstitutions & TypeScriptSubstitutionFlags.NonQualifiedEnumMembers) != 0 && IsTransformedEnumDeclaration(node))
        {
            _applicableSubstitutions |= TypeScriptSubstitutionFlags.NonQualifiedEnumMembers;
        }

        _previousOnEmitNode(hint, node, emitCallback);

        _applicableSubstitutions = savedApplicableSubstitutions;
        _currentSourceFile = savedCurrentSourceFile;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2641-2651 — <c>onSubstituteNode</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>hint === EmitHint.Expression</c> arm casts the node to
    /// <c>Expression</c>; the port states the same cast (the printer only asks for a substitution of that hint when the
    /// node is an expression, which is the contract the substitution table records).</remarks>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);
        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression((Expression)node);
        }
        else if (NodeTests.IsShorthandPropertyAssignment(node))
        {
            return SubstituteShorthandPropertyAssignment((ShorthandPropertyAssignment)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2653-2668 — <c>substituteShorthandPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>ObjectLiteralElementLike</c> answer is the port's <see cref="Node"/>
    /// (the visit-result base every element kind shares).</remarks>
    private Node SubstituteShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        if ((_enabledSubstitutions & TypeScriptSubstitutionFlags.NamespaceExports) != 0)
        {
            var name = node.Name!;
            var exportedName = TrySubstituteNamespaceExportedName(name);
            if (exportedName is not null)
            {
                // A shorthand property with an assignment initializer is probably part of a
                // destructuring assignment
                if (node.ObjectAssignmentInitializer is not null)
                {
                    var initializer = _factory.CreateAssignment(exportedName, node.ObjectAssignmentInitializer);
                    return Utilities.SetTextRange(_factory.CreatePropertyAssignment(name, initializer), node);
                }
                return Utilities.SetTextRange(_factory.CreatePropertyAssignment(name, exportedName), node);
            }
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2670-2681 — <c>substituteExpression</c>.</summary>
    private Expression SubstituteExpression(Expression node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                return SubstituteExpressionIdentifier((Identifier)node);
            case SyntaxKind.PropertyAccessExpression:
                return SubstitutePropertyAccessExpression((PropertyAccessExpression)node);
            case SyntaxKind.ElementAccessExpression:
                return SubstituteElementAccessExpression((ElementAccessExpression)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2683-2686 — <c>substituteExpressionIdentifier</c>.</summary>
    /// <remarks>PORT-NOTE: <c>trySubstituteNamespaceExportedName(node) || node</c> is a node-or-node fallback, which is
    /// the port's <c>??</c> (a node is never falsy in TS either).</remarks>
    private Expression SubstituteExpressionIdentifier(Identifier node)
        => TrySubstituteNamespaceExportedName(node) ?? node;

    /// <summary>ported from: src/compiler/transformers/ts.ts:2688-2707 — <c>trySubstituteNamespaceExportedName</c>.</summary>
    /// <remarks>PORT-NOTE: <c>enabledSubstitutions &amp; applicableSubstitutions</c> is a truthiness test on the bit
    /// intersection, stated as <c>!= 0</c>; the two <c>_applicableSubstitutions &amp; …</c> arms are the same test on the
    /// individual flags.</remarks>
    private Expression? TrySubstituteNamespaceExportedName(Identifier node)
    {
        // If this is explicitly a local name, do not substitute.
        if ((_enabledSubstitutions & _applicableSubstitutions) != 0 && !Utilities.IsGeneratedIdentifier(node) && !FactoryUtilities.IsLocalName(node))
        {
            // If we are nested within a namespace declaration, we may need to qualifiy
            // an identifier that is exported from a merged namespace.
            var container = _resolver.GetReferencedExportContainer(node, /*prefixLocals*/ false);
            if (container is not null && container.Kind != SyntaxKind.SourceFile)
            {
                var substitute = ((_applicableSubstitutions & TypeScriptSubstitutionFlags.NamespaceExports) != 0 && container.Kind == SyntaxKind.ModuleDeclaration)
                    || ((_applicableSubstitutions & TypeScriptSubstitutionFlags.NonQualifiedEnumMembers) != 0 && container.Kind == SyntaxKind.EnumDeclaration);
                if (substitute)
                {
                    return Utilities.SetTextRange(
                        _factory.CreatePropertyAccessExpression(_factory.GetGeneratedNameForNode(container, /*flags*/ null), node),
                        /*location*/ node);
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2709-2711 — <c>substitutePropertyAccessExpression</c>.</summary>
    private Expression SubstitutePropertyAccessExpression(PropertyAccessExpression node)
        => SubstituteConstantValue(node);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2713-2715 — <c>substituteElementAccessExpression</c>.</summary>
    private Expression SubstituteElementAccessExpression(ElementAccessExpression node)
        => SubstituteConstantValue(node);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2717-2719 — <c>safeMultiLineComment</c>.</summary>
    private static string SafeMultiLineComment(string value) => value.Replace("*/", "*_/");

    /// <summary>ported from: src/compiler/transformers/ts.ts:2721-2737 — <c>substituteConstantValue</c>.</summary>
    /// <remarks>PORT-NOTE: TS's union parameter
    /// (<c>PropertyAccessExpression | ElementAccessExpression</c>) has no model interface, so the port takes the
    /// <see cref="Expression"/> both arms are and returns it unchanged on the no-constant path. The numeric literal is
    /// built through <see cref="Js.NumberToString"/> for the reason
    /// <c>TypeScriptTransformer.TransformEnumMemberDeclarationValue</c> records.
    /// <c>setConstantValue</c> is <see cref="EmitNodes.SetConstantValue"/> and
    /// <c>getOriginalNode(node, isAccessExpression)</c> is the port's overload with the same test.</remarks>
    private Expression SubstituteConstantValue(Expression node)
    {
        var constantValue = TryGetConstEnumValue(node);
        if (constantValue is not null)
        {
            // track the constant value on the node for the printer in mayNeedDotDotForPropertyAccess
            EmitNodes.SetConstantValue(node, constantValue);
            Expression substitute = constantValue is string text
                ? _factory.CreateStringLiteral(text, /*isSingleQuote*/ null, /*hasExtendedUnicodeEscape*/ null)
                : constantValue is double number && number < 0
                    ? _factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, _factory.CreateNumericLiteral(Js.NumberToString(-number), /*numericLiteralFlags*/ null))
                    : _factory.CreateNumericLiteral(Js.NumberToString((double)constantValue!), /*numericLiteralFlags*/ null);

            if (!Js.IsTruthy(_compilerOptions.RemoveComments))
            {
                var originalNode = Utilities.GetOriginalNode(node, Utilities.IsAccessExpression);
                EmitNodes.AddSyntheticTrailingComment(substitute, SyntaxKind.MultiLineCommentTrivia, $" {SafeMultiLineComment(Utilities.GetTextOfNode(originalNode!))} ");
            }
            return substitute;
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2739-2745 — <c>tryGetConstEnumValue</c>.</summary>
    private object? TryGetConstEnumValue(Node node)
    {
        if (Utilities.GetIsolatedModules(_compilerOptions))
        {
            return null;
        }

        return NodeTests.IsPropertyAccessExpression(node) || NodeTests.IsElementAccessExpression(node)
            ? _resolver.GetConstantValue(node)
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2747-2749 — <c>shouldEmitAliasDeclaration</c>.</summary>
    private bool ShouldEmitAliasDeclaration(Node node)
        => Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax)
            || Utilities.IsInJSFile(node)
            || _resolver.IsReferencedAliasDeclaration(node, /*checkChildren*/ null);
}
