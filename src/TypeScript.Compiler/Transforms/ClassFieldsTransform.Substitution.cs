#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:3178-3316 — the printer hooks: the lexical-environment
// bookkeeping `onEmitNode` performs around each node, and the three substitutions (`this`, class aliases and the
// decorated-class `void 0`).
/// <summary>ported from: src/compiler/transformers/classFields.ts:3178-3316 — the emit and substitution hooks of
/// <c>transformClassFields</c>.</summary>
/// <remarks>
/// PORT-NOTE (the fall-through): the reference's <c>FunctionExpression</c> arm falls through into the
/// <c>FunctionDeclaration</c> arm when the node is neither an arrow function nor an async function body; the port states
/// that with <c>goto case</c>, which is C#'s spelling of the same control flow.
/// </remarks>
internal sealed partial class ClassFieldsTransformer
{
    /// <summary>ported from: src/compiler/transformers/classFields.ts:3178-3244 — <c>onEmitNode</c>.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        var original = Utilities.GetOriginalNode(node)!;
        if (_lexicalEnvironmentMap.TryGetValue(original, out var lex))
        {
            // If we've associated a lexical environment with the original node for this node, use it explicitly.
            var savedLexicalEnvironment = _lexicalEnvironment;
            var savedPreviousShouldSubstituteThisWithClassThis = _previousShouldSubstituteThisWithClassThis;
            _lexicalEnvironment = lex;
            _previousShouldSubstituteThisWithClassThis = _shouldSubstituteThisWithClassThis;
            _shouldSubstituteThisWithClassThis = !NodeTests.IsClassStaticBlockDeclaration(original)
                || (ES2015TransformSupport.GetInternalEmitFlags(original) & InternalEmitFlags.TransformPrivateStaticElements) == 0;
            _previousOnEmitNode(hint, node, emitCallback);
            _shouldSubstituteThisWithClassThis = _previousShouldSubstituteThisWithClassThis;
            _previousShouldSubstituteThisWithClassThis = savedPreviousShouldSubstituteThisWithClassThis;
            _lexicalEnvironment = savedLexicalEnvironment;
            return;
        }

        switch (node.Kind)
        {
            case SyntaxKind.FunctionExpression:
                if (NodeTests.IsArrowFunction(original) || (Utilities.GetEmitFlags(node) & EmitFlags.AsyncFunctionBody) != 0)
                {
                    // Arrow functions and functions that serve as the transformed body of an async function should
                    // preserve the outer lexical environment.
                    break;
                }
                // falls through
                goto case SyntaxKind.FunctionDeclaration;

            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.PropertyDeclaration:
                {
                    // Other function bodies and property declarations should clear the lexical environment.
                    // Note that this won't happen if a lexical environment was bound to the original node as that
                    // was handled above.
                    var savedLexicalEnvironment = _lexicalEnvironment;
                    var savedPreviousShouldSubstituteThisWithClassThis = _previousShouldSubstituteThisWithClassThis;
                    _lexicalEnvironment = null;
                    _previousShouldSubstituteThisWithClassThis = _shouldSubstituteThisWithClassThis;
                    _shouldSubstituteThisWithClassThis = false;
                    _previousOnEmitNode(hint, node, emitCallback);
                    _shouldSubstituteThisWithClassThis = _previousShouldSubstituteThisWithClassThis;
                    _previousShouldSubstituteThisWithClassThis = savedPreviousShouldSubstituteThisWithClassThis;
                    _lexicalEnvironment = savedLexicalEnvironment;
                    return;
                }

            case SyntaxKind.ComputedPropertyName:
                {
                    // Computed property names should use the outer lexical environment.
                    var savedLexicalEnvironment = _lexicalEnvironment;
                    var savedShouldSubstituteThisWithClassThis = _shouldSubstituteThisWithClassThis;
                    _lexicalEnvironment = (ClassFieldsLexicalEnvironment?)_lexicalEnvironment?.Previous;
                    _shouldSubstituteThisWithClassThis = _previousShouldSubstituteThisWithClassThis;
                    _previousOnEmitNode(hint, node, emitCallback);
                    _shouldSubstituteThisWithClassThis = savedShouldSubstituteThisWithClassThis;
                    _lexicalEnvironment = savedLexicalEnvironment;
                    return;
                }
        }
        _previousOnEmitNode(hint, node, emitCallback);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3246-3253 — <c>onSubstituteNode</c>.</summary>
    /// <remarks>Hooks node substitutions.</remarks>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);

        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression(node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3255-3263 — <c>substituteExpression</c>.</summary>
    private Node SubstituteExpression(Node node)
        => node.Kind switch
        {
            SyntaxKind.Identifier => SubstituteExpressionIdentifier((Identifier)node),
            SyntaxKind.ThisKeyword => SubstituteThisExpression((ThisExpression)node),
            _ => node,
        };

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3265-3287 — <c>substituteThisExpression</c>.</summary>
    private Expression SubstituteThisExpression(ThisExpression node)
    {
        if (
            (_enabledSubstitutions & ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference) != 0
            && _lexicalEnvironment?.Data is not null
            && !_noSubstitution.Contains(node))
        {
            var data = _lexicalEnvironment.Data;
            var substituteThis = _shouldSubstituteThisWithClassThis ? data.ClassThis ?? data.ClassConstructor : data.ClassConstructor;
            if (substituteThis is not null)
            {
                return Utilities.SetTextRange(
                    NodeFactory.SetOriginalNode(
                        _factory.CloneNode(substituteThis),
                        node),
                    node);
            }
            if ((data.Facts & ClassFieldsFacts.ClassWasDecorated) != 0 && _legacyDecorators)
            {
                return _factory.CreateParenthesizedExpression(_factory.CreateVoidZero());
            }
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3289-3291 — <c>substituteExpressionIdentifier</c>.</summary>
    private Expression SubstituteExpressionIdentifier(Identifier node)
        => TrySubstituteClassAlias(node) ?? node;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3293-3316 — <c>trySubstituteClassAlias</c>.</summary>
    private Expression? TrySubstituteClassAlias(Identifier node)
    {
        if ((_enabledSubstitutions & ClassPropertySubstitutionFlags.ClassAliases) != 0)
        {
            if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.ConstructorReference))
            {
                // Due to the emit for class decorators, any reference to the class from inside of the class body
                // must instead be rewritten to point to a temporary variable to avoid issues with the double-bind
                // behavior of class names in ES6.
                // Also, when emitting statics for class expressions, we must substitute a class alias for
                // constructor references in static property initializers.
                var declaration = _resolver.GetReferencedValueDeclaration(node);
                if (declaration is not null)
                {
                    // PORT-NOTE: TS's `declaration.id!` is `getNodeId(declaration)`, the index its sparse alias array
                    // uses; the port's dictionary is keyed by the same id.
                    if (_classAliases!.TryGetValue(Utilities.GetNodeId((Node)declaration), out var classAlias) && classAlias is not null)
                    {
                        var clone = _factory.CloneNode(classAlias);
                        EmitNodes.SetSourceMapRange(clone, TypeScriptTransformSupport.SourceMapRangeOf(node));
                        EmitNodes.SetCommentRange(clone, node);
                        return clone;
                    }
                }
            }
        }

        return null;
    }
}
