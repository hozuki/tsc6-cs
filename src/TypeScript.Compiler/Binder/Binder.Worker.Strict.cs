namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2881-2882, 2937-2948, 3000-3001 (the strict-mode arms of bindWorker's switch)
/// <summary>
/// The arms of <c>bindWorker</c> whose targets are the strict-mode and JSDoc checks — the members the M5-3 slice
/// ports (binder.ts:2114-2143, 2558-2749).
/// </summary>
/// <remarks>
/// PORT-NOTE: this segment and the registry it fills are separated from <c>Binder.Worker.cs</c> on purpose: the arms
/// call M5-3's members and nothing else, so the whole M5-3 surface of the dispatch is this file. The dispatcher
/// (<see cref="BindWorker"/>) never has to be touched when one of these arms changes.
/// <para>
/// PORT-NOTE: each arm passes the concrete class its kind proves, which is the argument TS's erased cast
/// (<c>node as CatchClause</c>) stands for. The callee may take a wider face; that is the callee's signature.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2881-2882 — <c>bindWorker</c>'s <c>PrivateIdentifier</c> arm.</summary>
    private static void BindPrivateIdentifierWorker(Binder binder, Node node)
        => binder.CheckPrivateIdentifier((PrivateIdentifier)node);

    /// <summary>ported from: src/compiler/binder.ts:2937-2938 — <c>bindWorker</c>'s <c>CatchClause</c> arm.</summary>
    private static void CheckStrictModeCatchClauseWorker(Binder binder, Node node)
        => binder.CheckStrictModeCatchClause((CatchClause)node);

    /// <summary>ported from: src/compiler/binder.ts:2939-2940 — <c>bindWorker</c>'s <c>DeleteExpression</c> arm.</summary>
    private static void CheckStrictModeDeleteExpressionWorker(Binder binder, Node node)
        => binder.CheckStrictModeDeleteExpression((DeleteExpression)node);

    /// <summary>ported from: src/compiler/binder.ts:2941-2942 — <c>bindWorker</c>'s <c>PostfixUnaryExpression</c> arm.</summary>
    private static void CheckStrictModePostfixUnaryExpressionWorker(Binder binder, Node node)
        => binder.CheckStrictModePostfixUnaryExpression((PostfixUnaryExpression)node);

    /// <summary>ported from: src/compiler/binder.ts:2943-2944 — <c>bindWorker</c>'s <c>PrefixUnaryExpression</c> arm.</summary>
    private static void CheckStrictModePrefixUnaryExpressionWorker(Binder binder, Node node)
        => binder.CheckStrictModePrefixUnaryExpression((PrefixUnaryExpression)node);

    /// <summary>ported from: src/compiler/binder.ts:2945-2946 — <c>bindWorker</c>'s <c>WithStatement</c> arm.</summary>
    private static void CheckStrictModeWithStatementWorker(Binder binder, Node node)
        => binder.CheckStrictModeWithStatement((WithStatement)node);

    /// <summary>ported from: src/compiler/binder.ts:2947-2948 — <c>bindWorker</c>'s <c>LabeledStatement</c> arm.</summary>
    private static void CheckStrictModeLabeledStatementWorker(Binder binder, Node node)
        => binder.CheckStrictModeLabeledStatement((LabeledStatement)node);

    /// <summary>ported from: src/compiler/binder.ts:3000-3001 — <c>bindWorker</c>'s <c>JSDocClassTag</c> arm.</summary>
    private static void BindJSDocClassTagWorker(Binder binder, Node node)
        => binder.BindJSDocClassTag((JSDocClassTag)node);

    /// <summary>ported from: src/compiler/binder.ts:2881-2882, 2937-2948, 3000-3001 — M5-3's kinds in the switch.</summary>
    private static void AddStrictModeWorkerHandlers(WorkerHandler?[] handlers)
    {
        // Strict mode checks
        Register(handlers, SyntaxKind.PrivateIdentifier, BindPrivateIdentifierWorker);
        Register(handlers, SyntaxKind.CatchClause, CheckStrictModeCatchClauseWorker);
        Register(handlers, SyntaxKind.DeleteExpression, CheckStrictModeDeleteExpressionWorker);
        Register(handlers, SyntaxKind.PostfixUnaryExpression, CheckStrictModePostfixUnaryExpressionWorker);
        Register(handlers, SyntaxKind.PrefixUnaryExpression, CheckStrictModePrefixUnaryExpressionWorker);
        Register(handlers, SyntaxKind.WithStatement, CheckStrictModeWithStatementWorker);
        Register(handlers, SyntaxKind.LabeledStatement, CheckStrictModeLabeledStatementWorker);

        // JSDoc
        Register(handlers, SyntaxKind.JSDocClassTag, BindJSDocClassTagWorker);
    }
}
