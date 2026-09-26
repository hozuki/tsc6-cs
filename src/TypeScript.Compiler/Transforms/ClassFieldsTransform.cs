#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:230-3360 — the whole classFields module.
// ported from: src/compiler/transformers/classFields.ts:230-242 (ClassPropertySubstitutionFlags), :244-318 (the private
// identifier info types), :297-307 (PrivateEnvironmentData), :320-331 (ClassLexicalEnvironment), :333-341 (ClassFacts),
// :342-343 (the two environment aliases), :354-3315 (transformClassFields, the table of closures), :3318-3360 (the
// module-level helpers: the three initializer/factory functions and the four predicates).
/// <summary>
/// ported from: src/compiler/transformers/classFields.ts:354-3315 — <c>transformClassFields</c>: the transformer that
/// rewrites ECMAScript class syntax — property declarations (with <c>useDefineForClassFields</c> deciding between Set
/// and Define semantics), <c>#private</c> fields/methods/accessors, <c>accessor</c> fields, class static blocks, and the
/// <c>this</c>/<c>super</c> references a static initializer loses when it is moved out of the class body.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformClassFields</c>, from
/// <c>transformers/classFields.ts</c>; its body is a table of closures over the factory's locals, so the port states the
/// factory as <see cref="ClassFieldsTransform.TransformClassFields"/> and the closures as the members of
/// <see cref="ClassFieldsTransformer"/> — the same "closures → classes" treatment <c>ES2015Transform</c> records.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c>, and the two arms of <c>chainBundle</c> are the kind test
/// <see cref="TransformationUtilities.ChainBundle"/> makes.
/// </para>
/// <para>
/// PORT-NOTE (<c>ClassFacts</c>): TS has two independent <c>ClassFacts</c> const enums — <c>ts.ts:219</c> and
/// <c>classFields.ts:333</c>. The port's <c>ts.ts</c> slice already owns the name (TypeScriptTransform.cs:80), so this
/// module's enum is stated as <see cref="ClassFieldsFacts"/> with the reference's own members and values; a
/// <c>PORT-NOTE</c> on the declaration records it.
/// </para>
/// <para>
/// PORT-NOTE (<c>getInternalEmitFlags</c>): the reference's helper is module-private in <c>es2015.ts</c> and the port's
/// first reader states it as <see cref="ES2015TransformSupport.GetInternalEmitFlags"/>; this slice calls that rather
/// than declaring a second copy.
/// </para>
/// </remarks>
internal static class ClassFieldsTransform
{
    /// <summary>ported from: src/compiler/transformers/classFields.ts:354-3450 — <c>transformClassFields</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c>; the returned member is the reference's own
    /// <c>chainBundle(context, transformSourceFile)</c> call, staying a method group so no delegate is allocated per
    /// call beyond the one <see cref="TransformationUtilities.ChainBundle"/> stores.
    /// </remarks>
    public static Func<object?, object?> TransformClassFields(ITransformerContext context)
        => new ClassFieldsTransformer(context).TransformSourceFileOrBundle;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3318-3325 — <c>createPrivateStaticFieldInitializer</c>.</summary>
    internal static Expression CreatePrivateStaticFieldInitializer(NodeFactory factory, Identifier variableName, Expression? initializer)
        => factory.CreateAssignment(
            variableName,
            factory.CreateObjectLiteralExpression(
                [
                    factory.CreatePropertyAssignment("value", initializer ?? factory.CreateVoidZero()),
                ],
                /*multiLine*/ null));

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3327-3333 — <c>createPrivateInstanceFieldInitializer</c>.</summary>
    internal static Expression CreatePrivateInstanceFieldInitializer(NodeFactory factory, Expression receiver, Expression? initializer, Identifier weakMapName)
        => factory.CreateCallExpression(
            factory.CreatePropertyAccessExpression(weakMapName, "set"),
            /*typeArguments*/ null,
            [receiver, initializer ?? factory.CreateVoidZero()]);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3335-3341 — <c>createPrivateInstanceMethodInitializer</c>.</summary>
    internal static Expression CreatePrivateInstanceMethodInitializer(NodeFactory factory, Expression receiver, Identifier weakSetName)
        => factory.CreateCallExpression(
            factory.CreatePropertyAccessExpression(weakSetName, "add"),
            /*typeArguments*/ null,
            [receiver]);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3343-3345 — <c>isReservedPrivateName</c>.</summary>
    internal static bool IsReservedPrivateName(PrivateIdentifier node)
        => !Utilities.IsGeneratedPrivateIdentifier(node) && node.EscapedText == "#constructor";

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3347-3352 — <c>PrivateIdentifierInExpression</c> and
    /// <c>isPrivateIdentifierInExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's intersection type <c>BinaryExpression &amp; { left: PrivateIdentifier; token: InKeyword }</c>
    /// has no C# spelling (§12.5); the guard answers <c>bool</c> and its reader narrows with the two tests it makes.</remarks>
    internal static bool IsPrivateIdentifierInExpression(BinaryExpression node)
        => NodeTests.IsPrivateIdentifier(node.Left!) && node.OperatorToken!.Kind == SyntaxKind.InKeyword;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3354-3356 — <c>isStaticPropertyDeclaration</c>.</summary>
    internal static bool IsStaticPropertyDeclaration(Node node)
        => NodeTests.IsPropertyDeclaration(node) && Utilities.HasStaticModifier(node);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3358-3360 — <c>isStaticPropertyDeclarationOrClassStaticBlock</c>.</summary>
    internal static bool IsStaticPropertyDeclarationOrClassStaticBlock(Node node)
        => NodeTests.IsClassStaticBlockDeclaration(node) || IsStaticPropertyDeclaration(node);
}

// ported from: src/compiler/transformers/classFields.ts:230-242 — the const enum `ClassPropertySubstitutionFlags`.
/// <summary>ported from: src/compiler/transformers/classFields.ts:230-242 — <c>ClassPropertySubstitutionFlags</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this const enum at the top of <c>transformers/classFields.ts</c>; the port
/// states it beside the transform with the same members in the same order, and <see cref="FlagsAttribute"/> because the
/// two members are combined with <c>|</c> and tested as a set.</remarks>
[Flags]
internal enum ClassPropertySubstitutionFlags
{
    /// <summary><c>ClassPropertySubstitutionFlags.None</c>.</summary>
    None = 0,

    /// <summary><c>ClassPropertySubstitutionFlags.ClassAliases</c> — enables substitutions for class expressions with
    /// static fields whose initializers reference the class name.</summary>
    ClassAliases = 1 << 0,

    /// <summary><c>ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference</c> — enables substitutions for class
    /// expressions with static fields whose initializers reference <c>this</c> or <c>super</c>.</summary>
    ClassStaticThisOrSuperReference = 1 << 1,
}

// ported from: src/compiler/transformers/classFields.ts:333-341 — the const enum `ClassFacts`.
/// <summary>ported from: src/compiler/transformers/classFields.ts:333-341 — <c>ClassFacts</c>.</summary>
/// <remarks>PORT-NOTE (the name): <c>ts.ts:219</c> declares a second, unrelated <c>ClassFacts</c> const enum and the
/// port's <c>TypeScriptTransform</c> already owns that name (TypeScriptTransform.cs:80), so this module's enum carries
/// the <c>ClassFields</c> prefix; its members and values are the reference's own.</remarks>
[Flags]
internal enum ClassFieldsFacts
{
    /// <summary><c>ClassFacts.None</c>.</summary>
    None = 0,

    /// <summary><c>ClassFacts.ClassWasDecorated</c>.</summary>
    ClassWasDecorated = 1 << 0,

    /// <summary><c>ClassFacts.NeedsClassConstructorReference</c>.</summary>
    NeedsClassConstructorReference = 1 << 1,

    /// <summary><c>ClassFacts.NeedsClassSuperReference</c>.</summary>
    NeedsClassSuperReference = 1 << 2,

    /// <summary><c>ClassFacts.NeedsSubstitutionForThisInClassStaticField</c>.</summary>
    NeedsSubstitutionForThisInClassStaticField = 1 << 3,

    /// <summary><c>ClassFacts.WillHoistInitializersToConstructor</c>.</summary>
    WillHoistInitializersToConstructor = 1 << 4,
}

// ported from: src/compiler/transformers/classFields.ts:244-318 — the private identifier info union.
/// <summary>ported from: src/compiler/transformers/classFields.ts:313-318 — <c>PrivateIdentifierInfo</c>.</summary>
/// <remarks>
/// PORT-NOTE (the union): TS's <c>PrivateIdentifierInfo</c> is the union of the four valid variants plus
/// <c>UntransformedPrivateIdentifierInfo</c> (whose only member is <c>kind: "untransformed"</c>). An untagged union has
/// no C# spelling, so the port states it as this abstract base with one class per variant and discriminates with a
/// type pattern — the same treatment <c>ES2015Transformer</c> records for the node unions it switches over. The base
/// carries no members because the <c>untransformed</c> variant has none.
/// </remarks>
internal abstract class PrivateIdentifierInfo
{
}

// ported from: src/compiler/transformers/classFields.ts:244-261 — `PrivateIdentifierInfoBase`.
/// <summary>ported from: src/compiler/transformers/classFields.ts:244-261 — <c>PrivateIdentifierInfoBase</c>.</summary>
/// <remarks>
/// <para>
/// <c>brandCheckIdentifier</c> can contain:
/// </para>
/// <list type="bullet">
/// <item><description>For instance field: the WeakMap that will be the storage for the field.</description></item>
/// <item><description>For instance methods or accessors: the WeakSet that will be used for brand checking.</description></item>
/// <item><description>For static members: the constructor that will be used for brand checking.</description></item>
/// </list>
/// <para>
/// PORT-NOTE: the reference's optional members are not optional in the variants that declare them, so the port holds
/// them as <c>required</c> init-only properties and the variants that mutate a previously stored entry
/// (<c>getterName</c>/<c>setterName</c>) make those two settable.
/// </para>
/// </remarks>
internal abstract class PrivateIdentifierInfoBase : PrivateIdentifierInfo
{
    /// <summary>ported from: classFields.ts:249 — <c>brandCheckIdentifier</c>.</summary>
    public required Identifier BrandCheckIdentifier { get; set; }

    /// <summary>ported from: classFields.ts:253 — <c>isStatic</c>: stores if the identifier is static or not.</summary>
    public required bool IsStatic { get; set; }

    /// <summary>ported from: classFields.ts:254-258 — <c>isValid</c>: reserved names (e.g. <c>#constructor</c>) or
    /// duplicate identifiers are considered invalid.</summary>
    public required bool IsValid { get; set; }
}

/// <summary>ported from: src/compiler/transformers/classFields.ts:263-273 — <c>PrivateIdentifierAccessorInfo</c>.</summary>
internal sealed class PrivateIdentifierAccessorInfo : PrivateIdentifierInfoBase
{
    /// <summary>ported from: classFields.ts:268 — <c>getterName</c>: the variable that will contain the private get
    /// accessor implementation, if any.</summary>
    public Identifier? GetterName { get; set; }

    /// <summary>ported from: classFields.ts:271 — <c>setterName</c>: the variable that will contain the private set
    /// accessor implementation, if any.</summary>
    public Identifier? SetterName { get; set; }
}

/// <summary>ported from: src/compiler/transformers/classFields.ts:275-281 — <c>PrivateIdentifierMethodInfo</c>.</summary>
internal sealed class PrivateIdentifierMethodInfo : PrivateIdentifierInfoBase
{
    /// <summary>ported from: classFields.ts:279 — <c>methodName</c>: the variable that will contain the private method
    /// implementation.</summary>
    public required Identifier MethodName { get; set; }
}

/// <summary>ported from: src/compiler/transformers/classFields.ts:283-286 — <c>PrivateIdentifierInstanceFieldInfo</c>.</summary>
internal sealed class PrivateIdentifierInstanceFieldInfo : PrivateIdentifierInfoBase
{
}

/// <summary>ported from: src/compiler/transformers/classFields.ts:288-295 — <c>PrivateIdentifierStaticFieldInfo</c>.</summary>
internal sealed class PrivateIdentifierStaticFieldInfo : PrivateIdentifierInfoBase
{
    /// <summary>ported from: classFields.ts:293 — <c>variableName</c>: the variable that will serve as the storage for
    /// the field.</summary>
    public required Identifier VariableName { get; set; }
}

/// <summary>ported from: src/compiler/transformers/classFields.ts:309-311 — <c>UntransformedPrivateIdentifierInfo</c>.</summary>
/// <remarks>PORT-NOTE: the reference's variant is the object literal <c>{ kind: "untransformed" }</c>; the port states
/// the constructible class and hands out one shared instance, since the variant carries no state.</remarks>
internal sealed class UntransformedPrivateIdentifierInfo : PrivateIdentifierInfo
{
    /// <summary>The singleton the environment stores for an untransformed private name.</summary>
    public static readonly UntransformedPrivateIdentifierInfo Instance = new();
}

// ported from: src/compiler/transformers/classFields.ts:297-307 — `PrivateEnvironmentData`.
/// <summary>ported from: src/compiler/transformers/classFields.ts:297-307 — <c>PrivateEnvironmentData</c>.</summary>
internal sealed class PrivateEnvironmentData
{
    /// <summary>ported from: classFields.ts:301 — <c>className</c>: used for prefixing generated variable names.</summary>
    public Identifier? ClassName { get; set; }

    /// <summary>ported from: classFields.ts:306 — <c>weakSetName</c>: used for brand check on private methods.</summary>
    public Identifier? WeakSetName { get; set; }
}

// ported from: src/compiler/transformers/classFields.ts:320-331 — `ClassLexicalEnvironment`.
/// <summary>ported from: src/compiler/transformers/classFields.ts:320-331 — <c>ClassLexicalEnvironment</c>.</summary>
internal sealed class ClassLexicalEnvironment
{
    /// <summary>ported from: classFields.ts:321 — <c>facts</c>.</summary>
    public ClassFieldsFacts Facts { get; set; }

    /// <summary>ported from: classFields.ts:325 — <c>classConstructor</c>: used for brand checks on static members and
    /// <c>this</c> references in static initializers.</summary>
    public Identifier? ClassConstructor { get; set; }

    /// <summary>ported from: classFields.ts:326 — <c>classThis</c>.</summary>
    public Identifier? ClassThis { get; set; }

    /// <summary>ported from: classFields.ts:330 — <c>superClassReference</c>: used for <c>super</c> references in static
    /// initializers.</summary>
    public Identifier? SuperClassReference { get; set; }
}

// ported from: src/compiler/transformers/classFields.ts:342-343 — the `LexicalEnv` alias.
/// <summary>ported from: src/compiler/transformers/classFields.ts:342-343 — <c>LexicalEnv</c>, the environment
/// <c>startClassLexicalEnvironment</c> pushes.</summary>
/// <remarks>
/// PORT-NOTE: the reference builds this with the object literal <c>{ previous: lexicalEnvironment, data: undefined }</c>
/// and its type is the <see cref="LexicalEnvironment{TEnvData,TPrivateEnvData,TPrivateEntry}"/> interface, which no port
/// type can construct as it stands; this is the constructible class that literal becomes (the same pairing
/// <c>TransformationUtilities.PrivateEnvironmentImpl</c> records for <c>newPrivateEnvironment</c>).
/// </remarks>
internal sealed class ClassFieldsLexicalEnvironment(ClassFieldsLexicalEnvironment? previous)
    : TransformationUtilities.LexicalEnvironment<ClassLexicalEnvironment?, PrivateEnvironmentData, PrivateIdentifierInfo>
{
    /// <summary>The literal's <c>data</c> slot.</summary>
    public ClassLexicalEnvironment? Data { get; set; }

    /// <summary>The literal's inherited <c>privateEnv</c> slot.</summary>
    public TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo>? PrivateEnv { get; set; }

    /// <summary>The literal's <c>previous</c> slot.</summary>
    public TransformationUtilities.LexicalEnvironment<ClassLexicalEnvironment?, PrivateEnvironmentData, PrivateIdentifierInfo>? Previous { get; } = previous;
}
