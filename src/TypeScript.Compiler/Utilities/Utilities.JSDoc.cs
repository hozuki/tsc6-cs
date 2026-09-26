using System;
using System.Collections.Generic;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:2996-3010 (isVariableLike)
// ported from: src/compiler/utilities.ts:4658-4682 (getJSDocCommentsAndTags)
// ported from: src/compiler/utilities.ts:4684-4695 (filterOwnedJSDocTags)
// ported from: src/compiler/utilities.ts:4701-4707 (ownsJSDocTag)
// ported from: src/compiler/utilities.ts:4710-4743 (getNextJSDocCommentLocation)
// ported from: src/compiler/utilitiesPublic.ts:1036-1053 (getJSDocParameterTagsWorker)
// ported from: src/compiler/utilitiesPublic.ts:1067-1074 (getJSDocParameterTags, getJSDocParameterTagsNoCache)
// ported from: src/compiler/utilitiesPublic.ts:1076-1098 (getJSDocTypeParameterTagsWorker, getJSDocTypeParameterTags, getJSDocTypeParameterTagsNoCache)
// ported from: src/compiler/utilitiesPublic.ts:1199-1201 (getJSDocSatisfiesTag)
// ported from: src/compiler/utilities.ts:11111-11124 (isJSDocSatisfiesExpression, getJSDocSatisfiesExpressionType,
//               tryGetJSDocSatisfiesTypeNode)
// ported from: src/compiler/utilitiesPublic.ts:1204-1211 (getJSDocTypeTag)
// ported from: src/compiler/utilitiesPublic.ts:1257-1281 (getJSDocTagsWorker, getJSDocTags, getFirstJSDocTag)
// ported from: src/compiler/utilitiesPublic.ts:2573-2575 (hasInitializer)
/// <summary>
/// The JSDoc lookup family: the walk up a node's JSDoc hosts that yields its comments and tags, and the tag
/// accessors built on it (<c>getJSDocTags</c>/<c>getJSDocTypeTag</c> and the parameter/template-tag variants).
/// </summary>
/// <remarks>
/// PORT-NOTE: the reference splits this family between <c>utilitiesPublic.ts</c> (the tag accessors) and
/// <c>utilities.ts</c> (the comment walk), and <c>utilities.ts</c> only re-exports the public half —
/// <c>utilities.ts:180-192</c> is that re-export list, which is why M5-1's note in <c>Binder.DeclarationBinders.cs</c>
/// and the M5-5/M5-4 notes cite <c>utilities.ts</c> lines for members defined in <c>utilitiesPublic.ts</c>. Each member
/// below carries the line of its *definition*.
/// <para>
/// PORT-NOTE: TS's union <c>JSDoc | JSDocTag</c> (and its <c>JSDocComment</c>) has no C# member; a comment/tag list is
/// stated as <see cref="INode"/>, the honest upper bound §12.9 records, and the one consumer that has to tell the two
/// apart (<c>getJSDocTagsWorker</c>'s flat map) tests the class. <c>NodeArray&lt;JSDocTag&gt;</c>/<c>NodeArray&lt;JSDoc&gt;</c>
/// are <c>List&lt;T&gt;</c> subclasses, so the <see cref="Core"/> helpers and <c>ReferenceEquals</c> answer TS's
/// <c>array</c>-identity tests — including <c>filter</c>'s documented "returns the input when nothing was filtered".
/// </para>
/// <para>
/// PORT-NOTE: <c>canHaveJSDoc</c> and <c>hasJSDocNodes</c> are the parser slice's members
/// (Utilities/Utilities.Parser.cs:89-90, :97) and are called, not re-declared, here; <c>getJSDocHost</c> and the four
/// <c>getSingle*</c>/<c>getSourceOf*</c>/<c>getNested*</c> helpers the comment walk reads are the diagnostic slice's
/// (Utilities/Utilities.Binder.Diagnostics.cs). TS's <c>emptyArray</c> is a shared singleton; the port states the
/// empty list each site needs.
/// </para>
/// <para>
/// PORT-NOTE: TS's type guards answer <c>bool</c> here (C# cannot narrow through a predicate) and are narrowed with
/// <c>Core.MapDefined</c> where TS's <c>filter</c> narrows, which is the same shape as <c>Core.Filter</c> for the
/// comment walk, whose "same instance out" contract the last branch depends on.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:2996-3010 — <c>isVariableLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to the <c>VariableLikeDeclaration</c> alias, which has no C# member; the port
    /// answers <c>bool</c> and the one caller (<c>getJSDocCommentsAndTags</c>) reads the initializer through the kind
    /// switch below.
    /// </remarks>
    public static bool IsVariableLike(Node node) => node.Kind
        is SyntaxKind.BindingElement
        or SyntaxKind.EnumMember
        or SyntaxKind.Parameter
        or SyntaxKind.PropertyAssignment
        or SyntaxKind.PropertyDeclaration
        or SyntaxKind.PropertySignature
        or SyntaxKind.ShorthandPropertyAssignment
        or SyntaxKind.VariableDeclaration;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2573-2575 — <c>hasInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard is one unchecked structural read (<c>!!(node as HasInitializer).initializer</c>); the
    /// model has no <c>HasInitializer</c> interface (§12), so the read is a pattern over the kinds that declare the
    /// slot — and since the only caller needs both the test and the value, this returns the value and the caller
    /// answers the test with a pattern. <c>ShorthandPropertyAssignment</c> is in <c>isVariableLike</c> but its
    /// initializer slot is named <c>objectAssignmentInitializer</c> upstream, so it is absent here too.
    /// </remarks>
    private static Expression? GetInitializer(Node node) => node switch
    {
        BindingElement bindingElement => bindingElement.Initializer,
        EnumMember enumMember => enumMember.Initializer,
        ParameterDeclaration parameter => parameter.Initializer,
        PropertyAssignment propertyAssignment => propertyAssignment.Initializer,
        PropertyDeclaration propertyDeclaration => propertyDeclaration.Initializer,
        PropertySignature propertySignature => propertySignature.Initializer,
        VariableDeclaration variableDeclaration => variableDeclaration.Initializer,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // The comment/tag walk
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:4658-4682 — <c>getJSDocCommentsAndTags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (the public one and the <c>@internal</c> one that adds <c>noCache</c>);
    /// the port states the optional parameter once. <c>hostNode.jsDoc</c> is reached through
    /// <see cref="IJSDocContainer"/> and narrowed to the concrete <see cref="JSDocArray"/>, the only implementation of
    /// <see cref="IJSDocArray"/> — the same treatment <c>bindJSDoc</c> and <c>hasJSDocNodes</c> give it. TS's
    /// <c>result || emptyArray</c> is <c>?? []</c> (a list is never falsy); the <c>node &amp;&amp; node.parent</c> loop
    /// guard drops to the walk ending at a parentless node, and TS's implicit <c>undefined</c> return is the explicit
    /// <c>null</c>.
    /// </remarks>
    public static IList<INode> GetJSDocCommentsAndTags(Node hostNode, bool noCache = false)
    {
        IList<INode>? result = null;
        // Pull parameter comments from declaring function as well
        if (IsVariableLike(hostNode) && GetInitializer(hostNode) is { } initializer && HasJSDocNodes(initializer))
        {
            result = Core.AddRange(result, FilterOwnedJSDocTags(hostNode, (JSDocArray)((IJSDocContainer)initializer).JsDoc!));
        }

        Node? node = hostNode;
        while (node is not null && node.Parent is not null)
        {
            if (HasJSDocNodes(node))
            {
                result = Core.AddRange(result, FilterOwnedJSDocTags(hostNode, (JSDocArray)((IJSDocContainer)node).JsDoc!));
            }

            if (node.Kind == SyntaxKind.Parameter)
            {
                var parameter = (ParameterDeclaration)node;
                List<JSDocParameterTag> parameterTags = noCache
                    ? GetJSDocParameterTagsNoCache(parameter)
                    : GetJSDocParameterTags(parameter);
                result = Core.AddRange<INode>(result, [.. parameterTags]);
                break;
            }
            if (node.Kind == SyntaxKind.TypeParameter)
            {
                var typeParameter = (TypeParameterDeclaration)node;
                List<JSDocTemplateTag> templateTags = noCache
                    ? GetJSDocTypeParameterTagsNoCache(typeParameter)
                    : GetJSDocTypeParameterTags(typeParameter);
                result = Core.AddRange<INode>(result, [.. templateTags]);
                break;
            }
            node = GetNextJSDocCommentLocation(node);
        }

        return result ?? [];
    }

    /// <summary>ported from: src/compiler/utilities.ts:4684-4695 — <c>filterOwnedJSDocTags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>flatMap</c> over the union <c>JSDoc | JSDocTag</c> becomes the explicit loop: a comment
    /// contributes either itself or its tags, and a tagless/undefined tag list contributes nothing — which is also
    /// what TS's <c>flatMap</c> does with a falsy mapped value. The port states it as a loop rather than
    /// <c>Core.FlatMapToMutable</c> because the two branches hand back different list types and the union's list
    /// would otherwise need a target-typed conditional over collection expressions. TS's <c>jsDoc.tags === ownedTags</c>
    /// is a real identity test — <c>filter</c>, and so <c>Core.Filter</c>, returns its input when nothing was filtered
    /// out (core.ts:277-296) — hence <c>ReferenceEquals</c>.
    /// </remarks>
    private static List<INode> FilterOwnedJSDocTags(Node hostNode, NodeArray<JSDoc> comments)
    {
        var lastJsDoc = Core.Last(comments);
        var result = new List<INode>(comments.Count);
        foreach (var jsDoc in comments)
        {
            if (jsDoc == lastJsDoc)
            {
                var ownedTags = Core.Filter<JSDocTag>(jsDoc.Tags, tag => OwnsJSDocTag(hostNode, tag));
                if (ReferenceEquals(jsDoc.Tags, ownedTags))
                {
                    result.Add(jsDoc);
                }
                else if (ownedTags is not null)
                {
                    result.AddRange(ownedTags);
                }
            }
            else
            {
                var overloadTags = Core.Filter<JSDocTag>(jsDoc.Tags, tag => NodeTests.IsJSDocOverloadTag(tag));
                if (overloadTags is not null)
                {
                    result.AddRange(overloadTags);
                }
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4701-4707 — <c>ownsJSDocTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard chain answers true as soon as any of its four negations holds; the port states the same
    /// chain as early returns. The last step compares the tag's grandparent with the host, and TS's implicit
    /// <c>undefined</c> parent (unreachable upstream — a tag under a JSDoc always has one) answers "not parenthesized"
    /// here, i.e. the callers' owns answer, which is what the guard above it already proved.
    /// </remarks>
    private static bool OwnsJSDocTag(Node hostNode, JSDocTag tag)
    {
        if (!(NodeTests.IsJSDocTypeTag(tag) || NodeTests.IsJSDocSatisfiesTag(tag))) return true;
        if (tag.Parent is not JSDoc jsDoc) return true;
        if (jsDoc.Parent is not Node parentOfJsDoc) return true;
        if (parentOfJsDoc is not ParenthesizedExpression parenthesizedExpression) return true;
        return parenthesizedExpression == hostNode;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4710-4743 — <c>getNextJSDocCommentLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.parent</c> is non-optional and its <c>parent.parent</c>/<c>parent.parent.parent</c>
    /// walks assume the chain; the port's parent slots are nullable, so a walk that runs out answers <c>null</c> (TS's
    /// implicit <c>undefined</c>), which is the same "no further location" answer.
    /// </remarks>
    public static Node? GetNextJSDocCommentLocation(Node node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return null;
        }

        if (
            parent.Kind == SyntaxKind.PropertyAssignment ||
            parent.Kind == SyntaxKind.ExportAssignment ||
            parent.Kind == SyntaxKind.PropertyDeclaration ||
            (parent.Kind == SyntaxKind.ExpressionStatement && node.Kind == SyntaxKind.PropertyAccessExpression) ||
            parent.Kind == SyntaxKind.ReturnStatement ||
            GetNestedModuleDeclaration(parent) is not null ||
            IsAssignmentExpression(node)
        )
        {
            return parent;
        }
        // Try to recognize this pattern when node is initializer of variable declaration and JSDoc comments are on containing variable statement.
        // /**
        //   * @param {number} name
        //   * @returns {number}
        //   */
        // var x = function(name) { return name.length; }
        else if (
            parent.Parent is not null &&
            (GetSingleVariableOfVariableStatement(parent.Parent) == node || IsAssignmentExpression(parent))
        )
        {
            return parent.Parent;
        }
        else if (
            parent.Parent?.Parent is not null &&
            (GetSingleVariableOfVariableStatement(parent.Parent.Parent) is not null ||
                GetSingleInitializerOfVariableStatementOrPropertyDeclaration(parent.Parent.Parent) == node ||
                GetSourceOfDefaultedAssignment(parent.Parent.Parent) is not null)
        )
        {
            return parent.Parent.Parent;
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The tag accessors
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1036-1053 — <c>getJSDocParameterTagsWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>filter</c> with a type-guard predicate narrows to <c>JSDocParameterTag[]</c>; the port's
    /// predicate answers <c>bool</c>, so <c>Core.MapDefined</c> does the narrowing (both skip the same elements — see
    /// the two helpers' remarks). TypeScript's <c>param.parent.parameters</c> is the signature face
    /// <see cref="ISignatureDeclarationBase.Parameters"/>.
    /// </remarks>
    private static List<JSDocParameterTag> GetJSDocParameterTagsWorker(ParameterDeclaration param, bool noCache = false)
    {
        if (param.Name is not null)
        {
            if (param.Name is Identifier name)
            {
                var escapedName = name.EscapedText;
                return Core.MapDefined<JSDocTag, JSDocParameterTag>(
                    GetJSDocTagsWorker(param.Parent!, noCache),
                    (tag, _) => tag is JSDocParameterTag parameterTag
                        && parameterTag.Name is Identifier tagName
                        && tagName.EscapedText == escapedName
                        ? parameterTag
                        : null);
            }
            else
            {
                var parent = (ISignatureDeclarationBase)param.Parent!;
                var i = parent.Parameters!.IndexOf(param);
                Debug.Assert(i > -1, "Parameters should always be in their parents' parameter list");
                var paramTags = Core.MapDefined<JSDocTag, JSDocParameterTag>(
                    GetJSDocTagsWorker(param.Parent!, noCache),
                    static (tag, _) => tag as JSDocParameterTag);
                if (i < paramTags.Count)
                {
                    return [paramTags[i]];
                }
            }
        }
        // return empty array for: out-of-order binding patterns and JSDoc function syntax, which has un-named parameters
        return [];
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1067-1069 — <c>getJSDocParameterTags</c>.</summary>
    public static List<JSDocParameterTag> GetJSDocParameterTags(ParameterDeclaration param)
        => GetJSDocParameterTagsWorker(param, /*noCache*/ false);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1071-1074 — <c>getJSDocParameterTagsNoCache</c>.</summary>
    public static List<JSDocParameterTag> GetJSDocParameterTagsNoCache(ParameterDeclaration param)
        => GetJSDocParameterTagsWorker(param, /*noCache*/ true);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1076-1079 — <c>getJSDocTypeParameterTagsWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as with the parameter worker, <c>Core.MapDefined</c> performs the type-guard filter. TS's
    /// <c>tag.typeParameters.some(...)</c> is <c>Core.Some</c>, which answers false for an absent list.
    /// </remarks>
    private static List<JSDocTemplateTag> GetJSDocTypeParameterTagsWorker(TypeParameterDeclaration param, bool noCache = false)
    {
        var name = param.Name!.EscapedText;
        return Core.MapDefined<JSDocTag, JSDocTemplateTag>(
            GetJSDocTagsWorker(param.Parent!, noCache),
            (tag, _) => tag is JSDocTemplateTag templateTag
                && Core.Some(templateTag.TypeParameters, typeParameter => typeParameter.Name?.EscapedText == name)
                ? templateTag
                : null);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1091-1093 — <c>getJSDocTypeParameterTags</c>.</summary>
    public static List<JSDocTemplateTag> GetJSDocTypeParameterTags(TypeParameterDeclaration param)
        => GetJSDocTypeParameterTagsWorker(param, /*noCache*/ false);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1095-1098 — <c>getJSDocTypeParameterTagsNoCache</c>.</summary>
    public static List<JSDocTemplateTag> GetJSDocTypeParameterTagsNoCache(TypeParameterDeclaration param)
        => GetJSDocTypeParameterTagsWorker(param, /*noCache*/ true);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1257-1271 — <c>getJSDocTagsWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS caches the computed tags on <c>node.jsDoc.jsDocCache</c> and creates the container's JSDoc list
    /// if it is absent (<c>node.jsDoc ??= []</c>); the port creates the concrete <see cref="JSDocArray"/> instead of a
    /// bare array, for the reason that type's own remarks give (it is the only implementation of
    /// <see cref="IJSDocArray"/>). TS's cache-miss test is <c>tags === undefined || noCache</c>, i.e. the port's
    /// <c>is null</c> (§5.1), and <c>Debug.assert(comments[0] !== comments[1])</c> is a reference comparison.
    /// <para>
    /// PORT-NOTE: <c>flatMap(comments, j => isJSDoc(j) ? j.tags : j)</c> is <c>Core.FlatMapToMutable</c>: a comment
    /// contributes its tag list, a tag contributes itself, and both TS's <c>flatMap</c> and the port's skip a falsy
    /// mapped value — so a tagless comment contributes nothing to either.
    /// </para>
    /// </remarks>
    private static List<JSDocTag> GetJSDocTagsWorker(Node node, bool noCache = false)
    {
        if (!CanHaveJSDoc(node))
        {
            return [];
        }

        var jsDocContainer = (IJSDocContainer)node;
        var tags = jsDocContainer.JsDoc?.JsDocCache;
        // If cache is 'null', that means we did the work of searching for JSDoc tags and came up with nothing.
        if (tags is null || noCache)
        {
            var comments = GetJSDocCommentsAndTags(node, noCache);
            Debug.Assert(comments.Count < 2 || comments[0] != comments[1]);
            tags = Core.FlatMapToMutable<INode, JSDocTag>(comments, (comment, _) => comment is JSDoc jsDoc ? jsDoc.Tags : comment);
            if (!noCache)
            {
                jsDocContainer.JsDoc ??= new JSDocArray();
                jsDocContainer.JsDoc.JsDocCache = tags;
            }
        }

        return tags;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1273-1276 — <c>getJSDocTags</c>.</summary>
    public static List<JSDocTag> GetJSDocTags(Node node) => GetJSDocTagsWorker(node, /*noCache*/ false);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1278-1281 — <c>getFirstJSDocTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's generic type-guard predicate narrows the found element; the port's predicate answers
    /// <c>bool</c>, so the result is the base <see cref="JSDocTag"/> and its one caller tests the class. TS's
    /// <c>find</c> is <c>Core.Find</c>.
    /// </remarks>
    private static JSDocTag? GetFirstJSDocTag(Node node, Func<JSDocTag, bool> predicate, bool noCache = false)
        => Core.Find(GetJSDocTagsWorker(node, noCache), (tag, _) => predicate(tag));

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1203-1211 — <c>getJSDocTypeTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tag.typeExpression &amp;&amp; tag.typeExpression.type</c> is the same two-step test on the
    /// tag it just found; the port states it as the pattern the found tag's class licenses.
    /// </remarks>
    public static JSDocTypeTag? GetJSDocTypeTag(Node node)
    {
        // We should have already issued an error if there were multiple type jsdocs, so just use the first one.
        var tag = GetFirstJSDocTag(node, static tag => NodeTests.IsJSDocTypeTag(tag));
        if (tag is JSDocTypeTag typeTag && typeTag.TypeExpression?.Type is not null)
        {
            return typeTag;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1199-1201 — <c>getJSDocSatisfiesTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getFirstJSDocTag</c> is this family's own private member (above) and <c>isJSDocSatisfiesTag</c> is
    /// the port's <see cref="NodeTests.IsJSDocSatisfiesTag"/>, whose <c>bool</c> answer finds the tag without narrowing
    /// it; the found tag is the matched class, which the cast states — the same shape <see cref="GetJSDocTypeTag"/> uses.
    /// </remarks>
    public static JSDocSatisfiesTag? GetJSDocSatisfiesTag(Node node)
        => GetFirstJSDocTag(node, static tag => NodeTests.IsJSDocSatisfiesTag(tag)) as JSDocSatisfiesTag;

    /// <summary>ported from: src/compiler/utilities.ts:11111-11113 — <c>isJSDocSatisfiesExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>JSDocSatisfiesExpression</c>, one of the widened-union names §15.15 lists
    /// (the port's parenthesized <c>@satisfies</c> expression is an ordinary <c>ParenthesizedExpression</c> whose JSDoc
    /// carries the tag); the member answers <c>bool</c> and the caller reads its type through
    /// <see cref="GetJSDocSatisfiesExpressionType"/>. <c>isInJSFile</c>/<c>isParenthesizedExpression</c>/<c>hasJSDocNodes</c>
    /// are <see cref="IsInJSFile"/>/<see cref="NodeTests.IsParenthesizedExpression"/>/<see cref="HasJSDocNodes"/>.
    /// </remarks>
    public static bool IsJSDocSatisfiesExpression(Node node)
        => IsInJSFile(node) && NodeTests.IsParenthesizedExpression(node) && HasJSDocNodes(node) && GetJSDocSatisfiesTag(node) is not null;

    /// <summary>ported from: src/compiler/utilities.ts:11116-11118 — <c>getJSDocSatisfiesExpressionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>JSDocSatisfiesExpression</c> face the port does not have; this member is
    /// reached only from the guard above, which takes a <c>Node</c>, so the parameter takes that face.
    /// <c>Debug.checkDefined</c> is <see cref="Debug.CheckDefined{T}(T?, string?)"/>.
    /// </remarks>
    public static ITypeNode GetJSDocSatisfiesExpressionType(Node node)
        => Debug.CheckDefined(TryGetJSDocSatisfiesTypeNode(node));

    /// <summary>ported from: src/compiler/utilities.ts:11121-11124 — <c>tryGetJSDocSatisfiesTypeNode</c>.</summary>
    public static ITypeNode? TryGetJSDocSatisfiesTypeNode(Node node)
        => GetJSDocSatisfiesTag(node)?.TypeExpression?.Type;
}
