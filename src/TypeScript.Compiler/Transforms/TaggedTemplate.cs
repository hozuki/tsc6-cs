#nullable enable

using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/taggedTemplate.ts:1-132 — the whole tagged-template transformer helper
/// <summary>
/// ported from: src/compiler/transformers/taggedTemplate.ts:35-132 — lowering a tagged template into a call whose first
/// argument is the cached template object: <c>processTaggedTemplateExpression</c> and the two string builders it needs.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports these as module-level functions of
/// <c>transformers/taggedTemplate.ts</c>, so they land on the class named after that module and are called qualified.
/// <see cref="ProcessLevel"/> is declared beside this class, the treatment <c>Model/AssignmentKind.cs</c> gives the
/// const enums of <c>utilities.ts</c>.
/// </para>
/// <para>
/// PORT-NOTE (the result union): TS's <c>CallExpression | TaggedTemplateExpression</c> has <c>LeftHandSideExpression</c>
/// as its common base in this model (<c>CallExpression</c> is one directly and <c>TaggedTemplateExpression</c> through
/// <see cref="MemberExpression"/>), so that is the port's answer — the two arms the body returns need no conversion.
/// </para>
/// <para>
/// PORT-NOTE (<c>visitEachChild</c>): the reference's early return visits the node with the reference's
/// <c>TransformationContext</c>; the port calls <see cref="Utilities.VisitEachChildWorker"/> with the same context, whose
/// factory slot is the concrete <see cref="NodeFactory"/> (see <see cref="ITransformationUtilitiesContext"/>).
/// </para>
/// <para>
/// PORT-NOTE (the newline regex): <c>text.replace(/\r\n?/g, "\n")</c> is a compile-time-constant pattern, so it is a
/// <c>[GeneratedRegex]</c> member (docs/architecture.md §5 rule 7) on this partial class.
/// </para>
/// </remarks>
internal static partial class TaggedTemplate
{
    /// <summary>ported from: src/compiler/transformers/taggedTemplate.ts:35-99 — <c>processTaggedTemplateExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>visitor</c> is the <c>Visitor</c> function type, which the port spells
    /// <c>Func&lt;Node, object?&gt;</c> (Utilities/Utilities.Visitor.cs), and its <c>template</c> is the
    /// <c>TemplateLiteral</c> union, which the model carries as a <see cref="MemberExpression"/> slot. The reference's
    /// <c>templateArguments = [undefined!]</c> hole is the port's <c>null!</c> in a <c>List&lt;Expression&gt;</c> — the
    /// same slot, filled before it is read.
    /// </remarks>
    public static LeftHandSideExpression ProcessTaggedTemplateExpression(
        ITransformationUtilitiesContext context,
        TaggedTemplateExpression node,
        Func<Node, object?> visitor,
        SourceFile currentSourceFile,
        Action<Identifier> recordTaggedTemplateString,
        ProcessLevel level)
    {
        // Visit the tag expression
        var tag = Utilities.VisitNode(node.Tag, visitor, Utilities.IsExpression);
        Debug.Assert(tag is not null);

        // Build up the template arguments and the raw and cooked strings for the template.
        // We start out with 'undefined' for the first argument and revisit later
        // to avoid walking over the template string twice and shifting all our arguments over after the fact.
        var templateArguments = new List<Expression> { null! };
        var cookedStrings = new List<Expression>();
        var rawStrings = new List<Expression>();
        var template = node.Template!;

        if (level == ProcessLevel.LiftRestriction && !Utilities.HasInvalidEscape(template))
        {
            return Utilities.VisitEachChildWorker(node, visitor, context);
        }

        var factory = context.Factory;

        if (NodeTests.IsNoSubstitutionTemplateLiteral(template))
        {
            cookedStrings.Add(CreateTemplateCooked(factory, template));
            rawStrings.Add(GetRawLiteral(factory, (ITemplateLiteralLikeNode)template, currentSourceFile));
        }
        else
        {
            var templateExpression = (TemplateExpression)template;
            cookedStrings.Add(CreateTemplateCooked(factory, templateExpression.Head!));
            rawStrings.Add(GetRawLiteral(factory, templateExpression.Head!, currentSourceFile));
            foreach (var templateSpan in templateExpression.TemplateSpans!)
            {
                cookedStrings.Add(CreateTemplateCooked(factory, templateSpan.Literal!));
                rawStrings.Add(GetRawLiteral(factory, (ITemplateLiteralLikeNode)templateSpan.Literal!, currentSourceFile));
                templateArguments.Add(Debug.CheckDefined(Utilities.VisitNode(templateSpan.Expression, visitor, Utilities.IsExpression)));
            }
        }

        var helperCall = context.GetEmitHelperFactory().CreateTemplateObjectHelper(
            factory.CreateArrayLiteralExpression(cookedStrings, /*multiLine*/ null),
            factory.CreateArrayLiteralExpression(rawStrings, /*multiLine*/ null));

        // Create a variable to cache the template object if we're in a module.
        // Do not do this in the global scope, as any variable we currently generate could conflict with
        // variables from outside of the current compilation. In the future, we can revisit this behavior.
        if (Parser.IsExternalModule(currentSourceFile))
        {
            var tempVar = factory.CreateUniqueName("templateObject", /*flags*/ null);
            recordTaggedTemplateString(tempVar);
            templateArguments[0] = factory.CreateLogicalOr(
                tempVar,
                factory.CreateAssignment(
                    tempVar,
                    helperCall));
        }
        else
        {
            templateArguments[0] = helperCall;
        }

        return factory.CreateCallExpression(tag, /*typeArguments*/ null, templateArguments);
    }

    /// <summary>ported from: src/compiler/transformers/taggedTemplate.ts:101-103 — <c>createTemplateCooked</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>template.templateFlags!</c> is <c>TokenFlags?</c> on the model's
    /// <see cref="ITemplateLiteralLikeNode"/>, and the reference's <c>undefined</c> would take the same arm anyway
    /// (JS reads <c>undefined &amp; mask</c> as <c>0</c>), so the port states <c>?? TokenFlags.None</c>. The
    /// template is the union <c>TemplateHead | TemplateMiddle | TemplateTail | NoSubstitutionTemplateLiteral</c>, which
    /// the port takes as <see cref="Node"/>, reading the two slots through the interface.</remarks>
    private static Expression CreateTemplateCooked(NodeFactory factory, Node template)
    {
        var templateLike = (ITemplateLiteralLikeNode)template;
        return ((templateLike.TemplateFlags ?? TokenFlags.None) & TokenFlags.IsInvalid) != 0
            ? factory.CreateVoidZero()
            : factory.CreateStringLiteral(templateLike.Text, /*isSingleQuote*/ null, /*hasExtendedUnicodeEscape*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/taggedTemplate.ts:110-132 — <c>getRawLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (substring): JS's <c>String.prototype.substring</c> clamps both bounds to <c>[0, length]</c> and swaps
    /// them when the start exceeds the end; C#'s <c>Substring</c> throws instead. <see cref="JsSubstring"/> states the
    /// clamped form the reference relies on — the slice is always non-negative for a real template node, but a
    /// synthesized one (the branch this runs in) need not be (§10.2 records the same JS/C# gap for JSDoc comments).
    /// </remarks>
    private static StringLiteral GetRawLiteral(NodeFactory factory, ITemplateLiteralLikeNode node, SourceFile currentSourceFile)
    {
        // Find original source text, since we need to emit the raw strings of the tagged template.
        // The raw strings contain the (escaped) strings of what the user wrote.
        // Examples: `\n` is converted to "\\n", a template string with a newline to "\n".
        var text = node.RawText;
        if (text is null)
        {
            Debug.AssertIsDefined(currentSourceFile, "Template literal node is missing 'rawText' and does not have a source file. Possibly bad transform.");
            text = Utilities.GetSourceTextOfNodeFromSourceFile(currentSourceFile, (Node)node);

            // text contains the original source, it will also contain quotes ("`"), dolar signs and braces ("${" and "}"),
            // thus we need to remove those characters.
            // First template piece starts with "`", others with "}"
            // Last template piece ends with "`", others with "${"
            var isLast = node.Kind is SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.TemplateTail;
            text = JsSubstring(text, 1, text.Length - (isLast ? 1 : 2));
        }

        // Newline normalization:
        // ES6 Spec 11.8.6.1 - Static Semantics of TV's and TRV's
        // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for both TV and TRV.
        text = NewlineRegExp().Replace(text, "\n");
        return Utilities.SetTextRange(factory.CreateStringLiteral(text, /*isSingleQuote*/ null, /*hasExtendedUnicodeEscape*/ null), (Node)node);
    }

    // ported from: src/compiler/transformers/taggedTemplate.ts:131 — <c>/\r\n?/g</c>.
    [GeneratedRegex(@"\r\n?")]
    private static partial Regex NewlineRegExp();

    /// <summary>The two-argument <c>String.prototype.substring</c> the reference's slice relies on.</summary>
    private static string JsSubstring(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return text.Substring(start, end - start);
    }
}

/// <summary>ported from: src/compiler/transformers/taggedTemplate.ts:29-32 — <c>ProcessLevel</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this enum in <c>transformers/taggedTemplate.ts</c>; the port states it beside
/// <see cref="TaggedTemplate"/> with the same members in the same order.</remarks>
internal enum ProcessLevel
{
    /// <summary><c>ProcessLevel.LiftRestriction</c>.</summary>
    LiftRestriction,

    /// <summary><c>ProcessLevel.All</c>.</summary>
    All,
}
