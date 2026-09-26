namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:8754-8786 — const enum ParsingContext
/// <summary>
/// The list a parse is currently inside, so the recursive descent knows which terminator to stop at.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this as a *sequence* (<c>SourceElements = 0</c> … <c>Count</c>) and builds the flag with
/// <c>1 &lt;&lt; ParsingContext.X</c>, so the members are indices rather than bits. The port keeps that shape, because
/// <see cref="Count"/> is used as a loop bound and as a "not a real context" marker; the mask is
/// <c>1 &lt;&lt; (int)context</c> wherever TS writes it. <see cref="ParserContexts"/> is the mask type.
/// </remarks>
internal enum ParsingContext
{
    /// <summary>Elements in a source file.</summary>
    SourceElements,
    /// <summary>Statements in a block.</summary>
    BlockStatements,
    /// <summary>Clauses in a switch statement.</summary>
    SwitchClauses,
    /// <summary>Statements in a switch clause.</summary>
    SwitchClauseStatements,
    /// <summary>Members in an interface or type literal.</summary>
    TypeMembers,
    /// <summary>Members in a class declaration.</summary>
    ClassMembers,
    /// <summary>Members in an enum declaration.</summary>
    EnumMembers,
    /// <summary>Elements in a heritage clause.</summary>
    HeritageClauseElement,
    /// <summary>Variable declarations in a variable statement.</summary>
    VariableDeclarations,
    /// <summary>Binding elements in an object binding list.</summary>
    ObjectBindingElements,
    /// <summary>Binding elements in an array binding list.</summary>
    ArrayBindingElements,
    /// <summary>Expressions in an argument list.</summary>
    ArgumentExpressions,
    /// <summary>Members in an object literal.</summary>
    ObjectLiteralMembers,
    /// <summary>Attributes in a JSX element.</summary>
    JsxAttributes,
    /// <summary>Things between opening and closing JSX tags.</summary>
    JsxChildren,
    /// <summary>Members in an array literal.</summary>
    ArrayLiteralMembers,
    /// <summary>Parameters in a parameter list.</summary>
    Parameters,
    /// <summary>JSDoc parameters in the parameter list of a JSDoc function type.</summary>
    JSDocParameters,
    /// <summary>Property names in a rest type list.</summary>
    RestProperties,
    /// <summary>Type parameters in a type parameter list.</summary>
    TypeParameters,
    /// <summary>Type arguments in a type argument list.</summary>
    TypeArguments,
    /// <summary>Element types in a tuple element type list.</summary>
    TupleElementTypes,
    /// <summary>Heritage clauses for a class or interface declaration.</summary>
    HeritageClauses,
    /// <summary>A named import clause's import specifier list.</summary>
    ImportOrExportSpecifiers,
    /// <summary>Import attributes.</summary>
    ImportAttributes,
    /// <summary>Parsing via <c>JSDocParser</c>.</summary>
    JSDocComment,
    /// <summary>Not a context: the number of them, for the loops that walk them all.</summary>
    Count,
}

// ported from: src/compiler/parser.ts:414-419 — const enum SpeculationKind
/// <summary>What a rollback of the parser state is for.</summary>
internal enum SpeculationKind
{
    TryParse,
    Lookahead,
    Reparse,
}

// ported from: src/compiler/parser.ts:405-413 — const enum SignatureFlags
/// <summary>
/// The contexts a parameter list is parsed in.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS calls this <c>SignatureFlags</c>, but the port already has a <c>SignatureFlags</c> from
/// <c>types.ts</c> (the flags of a resolved signature), and both live in the one flat namespace. The parser's enum is
/// prefixed to keep them apart, the same way <c>Queue</c> became <c>TsQueue</c> (§10.1).
/// </remarks>
[Flags]
internal enum ParserSignatureFlags
{
    None = 0,
    Yield = 1 << 0,
    Await = 1 << 1,
    Type = 1 << 2,
    IgnoreMissingOpenBrace = 1 << 4,
    JSDoc = 1 << 5,
}

// ported from: src/compiler/parser.ts:8784-8788 — const enum Tristate
/// <summary>Three-valued logic, for the JSDoc parser's optional-tag bookkeeping.</summary>
internal enum Tristate
{
    False,
    True,
    Unknown,
}
