namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:4842-4846
/// <summary>
/// Which kind of assignment a node is the target of. Equivalent to TS's <c>AssignmentKind</c> const enum.
/// </summary>
/// <remarks>
/// PORT-NOTE: this is a module-level enum of <c>utilities.ts</c>, which is not among the two utilities.ts enums
/// <c>tools/Gen</c> names (<c>Associativity</c>, <c>OperatorPrecedence</c>), so it is stated by hand here — beside the
/// generated <see cref="AssignmentDeclarationKind"/> it is read with. Adding it to that tool's list is what would turn
/// this file generated; until then its only writers are the two members that answer it, <c>Utilities.GetAssignmentTargetKind</c>
/// (Checker/Utilities.Checker.cs) and the checker's read-only-entity test.
/// <para>
/// PORT-NOTE: TS's members are the implied values 0/1/2 of a <c>const enum</c>; the port writes them out, as it does
/// for every other enum (§4.1).
/// </para>
/// </remarks>
internal enum AssignmentKind : int
{
    /// <summary>Not an assignment target at all.</summary>
    None = 0,
    /// <summary>A definite assignment: <c>=</c>, <c>+=</c>, <c>??=</c>, the head of a <c>for…in</c>/<c>for…of</c>.</summary>
    Definite = 1,
    /// <summary>A compound assignment: any other operator, or <c>++</c>/<c>--</c>.</summary>
    Compound = 2,
}
