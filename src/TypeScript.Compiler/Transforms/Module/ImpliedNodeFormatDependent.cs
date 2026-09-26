#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:34-99 — the whole file.
/// <summary>
/// ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:34-99 —
/// <c>transformImpliedNodeFormatDependentModule</c>: the transformer that runs <c>transformECMAScriptModule</c> or
/// <c>transformModule</c> per file, choosing by the file's <em>emit</em> module format, and routes the two per-node
/// hooks to whichever of the two it picked.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function whose body is a table of closures, so the port states
/// the factory and the class that owns the closures — the same treatment <c>EsNextAnd2015</c> and
/// <c>ModuleTransform</c> record.
/// </para>
/// <para>
/// PORT-NOTE (the format query): the reference reads <c>context.getEmitHost().getEmitModuleFormatOfFile(file)</c>, which
/// is <see cref="IEmitHost.GetEmitModuleFormatOfFile"/>, already carried by the port's emit host
/// (<c>Program/Program.Emit.cs:138</c> answers it with <c>Program.GetEmitModuleFormatOfFileWorker</c>); the port adds
/// no new context member for it.
/// </para>
/// <para>
/// PORT-NOTE (both component transforms run): the reference builds <em>both</em> transformers on this same context and
/// then restores the two hooks it captured, because each factory installs its own hooks. The port states that same
/// sequence, so both transformers' emit nodes/helpers are installed exactly as the reference leaves them.
/// </para>
/// </remarks>
internal static class ImpliedNodeFormatDependent
{
    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:34-38 —
    /// <c>transformImpliedNodeFormatDependentModule</c>.</summary>
    public static Func<object?, object?> TransformImpliedNodeFormatDependentModule(ITransformerContext context)
        => new ImpliedNodeFormatDependentTransformer(context).TransformSourceFileOrBundle;

    /// <summary>ported from: src/compiler/transformer.ts:78-116 — <c>getModuleTransformer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home, §15.15 "the reader declares it"): TS declares this in <c>transformer.ts</c>, whose port is the
    /// emit-pipeline shell (<c>Transforms/Transformer.cs</c>), where it is still the M7-B named blocker. It is stated
    /// here because the four module transformers are this slice's own family, so the shell's member becomes the one
    /// line <c>=> ImpliedNodeFormatDependent.GetModuleTransformer(moduleKind);</c> and the selection is testable (and
    /// test-covered) before that shell lands.
    /// <para>
    /// The arms are the reference's: <c>preserve</c> keeps CJS input syntax and is therefore the ES-module transform;
    /// every fixed ES/Node/CommonJS kind defers to the per-file implied-format chooser; <c>system</c> is the
    /// <c>System.register</c> wrapper; and everything else (AMD, UMD, None, the Node kinds the reference does not name)
    /// is the CommonJS/AMD/UMD transform, whose own <c>getTransformModuleDelegate</c> splits those three.
    /// </para>
    /// </remarks>
    public static Func<ITransformerContext, Func<object?, object?>> GetModuleTransformer(ModuleKind moduleKind)
        => moduleKind switch
        {
            ModuleKind.Preserve => EsNextAnd2015.TransformECMAScriptModule,
            ModuleKind.ESNext
                or ModuleKind.ES2022
                or ModuleKind.ES2020
                or ModuleKind.ES2015
                or ModuleKind.Node16
                or ModuleKind.Node18
                or ModuleKind.Node20
                or ModuleKind.NodeNext
                or ModuleKind.CommonJS
                // Wraps `transformModule` and `transformECMAScriptModule` and
                // selects between them based on the `impliedNodeFormat` of the
                // source file.
                => TransformImpliedNodeFormatDependentModule,
            ModuleKind.System => SystemTransform.TransformSystemModule,
            _ => ModuleTransform.TransformModule,
        };
}

/// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:34-99 — the body of
/// <c>transformImpliedNodeFormatDependentModule</c>.</summary>
internal sealed class ImpliedNodeFormatDependentTransformer
{
    private readonly ITransformerContext _context;
    private readonly Func<object?, object?> _esmTransform;
    private readonly Func<object?, object?> _cjsTransform;
    private readonly Func<EmitHint, Node, Node> _esmOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _esmOnEmitNode;
    private readonly Func<EmitHint, Node, Node> _cjsOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _cjsOnEmitNode;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:59 — <c>currentSourceFile</c>.</summary>
    private SourceFile? _currentSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:37-63 — the factory's prologue.</summary>
    public ImpliedNodeFormatDependentTransformer(ITransformerContext context)
    {
        _context = context;
        var previousOnSubstituteNode = context.OnSubstituteNode;
        var previousOnEmitNode = context.OnEmitNode;
        _previousOnSubstituteNode = previousOnSubstituteNode!;
        _previousOnEmitNode = previousOnEmitNode!;

        _esmTransform = EsNextAnd2015.TransformECMAScriptModule(context);

        _esmOnSubstituteNode = context.OnSubstituteNode!;
        _esmOnEmitNode = context.OnEmitNode!;

        context.OnSubstituteNode = _previousOnSubstituteNode;
        context.OnEmitNode = _previousOnEmitNode;

        _cjsTransform = ModuleTransform.TransformModule(context);

        _cjsOnSubstituteNode = context.OnSubstituteNode!;
        _cjsOnEmitNode = context.OnEmitNode!;

        context.OnSubstituteNode = OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;
        context.EnableSubstitution(SyntaxKind.SourceFile);
        context.EnableEmitNotification(SyntaxKind.SourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:62 — the returned
    /// <c>transformSourceFileOrBundle</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node)
        => ((Node)node!).Kind == SyntaxKind.SourceFile
            ? TransformSourceFile((SourceFile)node!)
            : TransformBundle((Bundle)node!);

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:64-80 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        if (NodeTests.IsSourceFile(node))
        {
            _currentSourceFile = (SourceFile)node;
            // Neither component transform wants substitution notifications for `SourceFile`s, and, in fact, relies on
            // the source file emit notification to setup scope variables for substitutions (so we _cannot_ call their substitute
            // functions on source files safely, as that context only gets setup in a later pipeline phase!)
            return _previousOnSubstituteNode(hint, node);
        }
        else
        {
            if (_currentSourceFile is null)
            {
                return _previousOnSubstituteNode(hint, node);
            }
            if (GetEmitModuleFormatOfFile(_currentSourceFile) >= ModuleKind.ES2015)
            {
                return _esmOnSubstituteNode(hint, node);
            }
            return _cjsOnSubstituteNode(hint, node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:82-92 — <c>onEmitNode</c>.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (NodeTests.IsSourceFile(node))
        {
            _currentSourceFile = (SourceFile)node;
        }
        if (_currentSourceFile is null)
        {
            _previousOnEmitNode(hint, node, emitCallback);
            return;
        }
        if (GetEmitModuleFormatOfFile(_currentSourceFile) >= ModuleKind.ES2015)
        {
            _esmOnEmitNode(hint, node, emitCallback);
            return;
        }
        _cjsOnEmitNode(hint, node, emitCallback);
    }

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:94-96 — <c>getModuleTransformForFile</c>.</summary>
    private Func<object?, object?> GetModuleTransformForFile(SourceFile file)
        => GetEmitModuleFormatOfFile(file) >= ModuleKind.ES2015 ? _esmTransform : _cjsTransform;

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:98-106 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _currentSourceFile = node;
        var result = GetModuleTransformForFile(node)(node);
        _currentSourceFile = null;
        Debug.Assert(NodeTests.IsSourceFile((Node)result!), "Expected a SourceFile");
        return (SourceFile)result!;
    }

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:108-112 — <c>transformBundle</c>.</summary>
    private Bundle TransformBundle(Bundle node)
        => _context.Factory.CreateBundle(Core.Map(node.SourceFiles, (sourceFile, _) => TransformSourceFile(sourceFile)));

    /// <summary>ported from: src/compiler/transformers/module/impliedNodeFormatDependent.ts:56 — the
    /// <c>getEmitModuleFormatOfFile</c> closure.</summary>
    private ModuleKind GetEmitModuleFormatOfFile(SourceFile file)
        => _context.GetEmitHost().GetEmitModuleFormatOfFile(file);
}
