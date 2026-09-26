namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:9559, 9562 and 9605 (TransformationContext) — the three members
// factory/emitHelpers.ts reads out of the full context.
/// <summary>
/// The slice of <c>TransformationContext</c> that <c>factory/emitHelpers.ts</c> reads.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>createEmitHelperFactory(context: TransformationContext)</c>, and its whole body reads three
/// members from that context — <c>context.factory</c> (every <c>create*</c> call and the caching in <c>emitHelpers()</c>),
/// <c>context.requestEmitHelper</c> (once per helper it asks for), and <c>context.getCompilerOptions()</c> (twice, to
/// choose helper text). The generated <see cref="ITransformationContext"/> also needs an <see cref="IEmitResolver"/>, an
/// <see cref="IEmitHost"/> and a real emitter, none of which exist before M7, so starting from it would make the
/// emit-helper factory impossible to write or exercise. This interface is that three-member slice — the same treatment
/// <c>IReadonlyPragmaContext</c> gives <c>parsePragmasIntoFields</c>, and <see cref="EmitHelper"/>'s class is to TS's
/// descriptor objects.
/// <para>
/// PORT-NOTE: <see cref="Factory"/> is the concrete <see cref="NodeFactory"/> rather than <c>INodeFactory</c>. The port's
/// factory does not implement that interface yet (docs/architecture.md §15.9 records the six <c>getName</c> members and
/// the <c>CreateNodeArray&lt;T&gt;</c> constraint that keep the two apart), so an <c>INodeFactory</c>-typed slot could not
/// hold the only factory the port has; the concrete type names every <c>create*</c> member the helpers call.
/// </para>
/// <para>
/// <c>getCompilerOptions()</c> is declared even though the option model itself arrives with M8: the two reads select
/// helper text (<c>getEmitScriptTarget(options)</c> for the <c>__assign</c> and <c>__spreadArray</c> shapes, and the JSX
/// <c>preserve</c> test), so they cannot be dropped from the slice.
/// </para>
/// </remarks>
internal interface IEmitHelperFactoryContext
{
    /// <summary>The factory the helper calls are built with.</summary>
    NodeFactory Factory { get; }

    /// <summary>The compiler options the two text-selecting reads consult.</summary>
    ICompilerOptions GetCompilerOptions();

    /// <summary>Records that the node being transformed needs <paramref name="helper"/>.</summary>
    void RequestEmitHelper(Node? helper);
}
