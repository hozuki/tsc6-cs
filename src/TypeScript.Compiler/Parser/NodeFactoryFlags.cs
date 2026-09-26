namespace TypeScript.Compiler;

// ported from: src/compiler/factory/nodeFactory.ts:466-476 (const enum NodeFactoryFlags)
/// <summary>Flags that control how a <see cref="NodeFactory"/> behaves.</summary>
/// <remarks>
/// Hand-written: TS declares this as a `const enum` inside <c>nodeFactory.ts</c>, which is outside the files the
/// type model is extracted from, so the generated <c>INodeFactory.Flags</c> sees it as <c>object?</c>. The
/// hand-written factory declares it properly — see <c>docs/architecture.md</c> §12.5 item 4.
/// </remarks>
internal enum NodeFactoryFlags
{
    None = 0,

    /// <summary>Disables the parenthesizer rules for the factory.</summary>
    NoParenthesizerRules = 1 << 0,

    /// <summary>Disables the node converters for the factory.</summary>
    NoNodeConverters = 1 << 1,

    /// <summary>Ensures new <c>PropertyAccessExpression</c> nodes are created with the <c>NoIndentation</c> emit flag set.</summary>
    NoIndentationOnFreshPropertyAccess = 1 << 2,

    /// <summary>Do not set an <c>original</c> pointer when updating a node.</summary>
    NoOriginalNode = 1 << 3,
}
