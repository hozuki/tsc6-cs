namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:9048-9054 (the `target` computed option), 9291 (getEmitScriptTarget)
// ported from: src/compiler/utilities.ts:9271-9276 (the `alwaysStrict` computed option), 9323 (getAlwaysStrict)
// ported from: src/compiler/utilities.ts:9358-9366 (StrictOptionName), 9369-9371 (getStrictOptionValue)
// ported from: src/compiler/utilities.ts:9406-9409 (getJSXTransformEnabled)
/// <summary>
/// The option readers Layer 4 needs, plus the strict-flag reader the checker and the later layers share.
/// </summary>
/// <remarks>
/// PORT-NOTE: the options model itself is the generated <see cref="ICompilerOptions"/> — the interface
/// <c>types.ts:7403-7572</c> declares and <c>tools/Gen</c> emits into <c>Model/NodeInterfaces.cs</c>. The port has no
/// concrete options class: the reference fills that interface in with object literals, nothing in this slice
/// constructs one, and the layer that will (M8's tsconfig reader, or a caller of the binder) is where such a type
/// belongs if it turns out to be needed. These helpers therefore take and read the interface itself, the way every
/// slot is typed by it elsewhere.
/// <para>
/// PORT-NOTE: on the reference side only one of these is a function in its own right. <c>getEmitScriptTarget</c> and
/// <c>getAlwaysStrict</c> are two of the entries of the <c>createComputedCompilerOptions</c> table (the factory is at
/// utilities.ts:9030-9039, the table it builds at 9041-9283), re-exported by name at 9291 / 9323; that table memoizes
/// each <c>computeValue</c> per options object and declares the reads it has to re-run when another option changes
/// (<c>dependencies</c>). Neither half is ported: the memo is keyed on the whole options object, and the metadata is
/// only consulted by M8's build/watch paths, which is where it belongs. Each exported name therefore becomes a method
/// whose body is its closure's body — the same rearrangement §15.9 used for the <c>utilities.ts</c> free functions,
/// and it keeps the call sites (<c>getEmitScriptTarget(options)</c>) reading like the reference.
/// </para>
/// <para>
/// PORT-NOTE: the table has 26 entries and three of them are here: <c>target</c> and <c>alwaysStrict</c> are the first
/// two methods below, and the eight strict-family entries — <c>noImplicitAny</c>, <c>noImplicitThis</c>,
/// <c>strictNullChecks</c>, <c>strictFunctionTypes</c>, <c>strictBindCallApply</c>, <c>strictPropertyInitialization</c>,
/// <c>strictBuiltinIteratorReturn</c>, <c>useUnknownInCatchVariables</c> — all just call <c>getStrictOptionValue</c>,
/// which is the third. The other 16 — <c>module</c>, <c>moduleResolution</c>, <c>moduleDetection</c>,
/// <c>isolatedModules</c>, <c>esModuleInterop</c>, <c>allowSyntheticDefaultImports</c>,
/// <c>resolvePackageJsonExports</c>, <c>resolvePackageJsonImports</c>, <c>resolveJsonModule</c>,
/// <c>declaration</c>, <c>preserveConstEnums</c>, <c>incremental</c>, <c>declarationMap</c>, <c>allowJs</c>,
/// <c>useDefineForClassFields</c>, <c>allowImportingTsExtensions</c> — are deliberately not here: each belongs to the
/// layer that reads it (M5's <c>moduleNameResolver</c>, M6's checker, M8's CLI) and each is a copy of its own closure,
/// so porting them now would add unread bodies. What is here is the set §15.12's first planning finding names plus the
/// strict reader: the binder reads exactly <c>getEmitScriptTarget</c> (binder.ts:574, 2739) and <c>getAlwaysStrict</c>
/// (binder.ts:621), and <c>getStrictOptionValue</c> is what the eight strict-family entries beside <c>alwaysStrict</c>
/// in that table are defined in terms of in the reference.
/// </para>
/// <para>
/// PORT-NOTE: TS declares every one of these parameters non-optional and reads straight through it, so a missing
/// options object throws upstream (a <c>TypeError</c>) the way a <c>null</c> one throws here. The port's parameters
/// are nullable — M8's callers build the object before filling it, and a caller with no program has none at all — so
/// each body asserts the read (<c>!</c>, the explicit non-null of §5's assertion row) rather than substituting a
/// default: answering "the default target" for a missing options object would turn a caller bug into a plausible
/// value. Every slot the reads touch is a member of the generated interface, so a generator change that dropped one
/// would fail this file's compilation rather than silently answer a default.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:9048-9054 — the <c>target</c> entry of
    /// <c>createComputedCompilerOptions</c>, exported as <c>getEmitScriptTarget</c> at 9291.</summary>
    /// <remarks>
    /// PORT-NOTE: ES3 is read as "no target at all" — <c>target === ScriptTarget.ES3 ? undefined : target</c> — so the
    /// fallback is <see cref="ScriptTarget.LatestStandard"/> (ES2025), never ES3 and never ES5.
    /// <c>LatestStandard</c> is an alias of ES2025 in the generated enum, so the answer is spelled the way the
    /// reference spells it. Checked against <c>lib/typescript.js</c>: <c>getEmitScriptTarget({})</c> and
    /// <c>getEmitScriptTarget({ target: 0 })</c> both answer 12, <c>{ target: 1 }</c> answers 1, <c>{ target: 2 }</c>
    /// answers 2 and <c>{ target: 99 }</c> answers 99.
    /// </remarks>
    public static ScriptTarget GetEmitScriptTarget(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        var target = compilerOptions.Target == ScriptTarget.ES3 ? (ScriptTarget?)null : compilerOptions.Target;
        return target ?? ScriptTarget.LatestStandard;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9271-9276 — the <c>alwaysStrict</c> entry of
    /// <c>createComputedCompilerOptions</c>, exported as <c>getAlwaysStrict</c> at 9323.</summary>
    /// <remarks>
    /// PORT-NOTE: the test is <c>alwaysStrict !== false</c>, not <c>!!alwaysStrict</c>: an unset slot means <em>on</em>
    /// (the reference keeps the value this option computed when it was one of the strict-family flags, and says so in
    /// the comment above the entry). With a <c>bool?</c> slot, <c>!= false</c> is <c>null != false</c> → <c>true</c>,
    /// which is the same answer. Checked against <c>lib/typescript.js</c>: <c>getAlwaysStrict({})</c> is
    /// <c>true</c>, <c>{ alwaysStrict: false }</c> is <c>false</c>, and the <c>strict</c> slot does not enter the
    /// answer (<c>{ strict: false }</c> is still <c>true</c>).
    /// </remarks>
    public static bool GetAlwaysStrict(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return compilerOptions.AlwaysStrict != false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9369-9371 — <c>getStrictOptionValue</c> (the key type it
    /// switches on, <c>StrictOptionName</c>, is at 9358-9366).</summary>
    /// <remarks>
    /// PORT-NOTE: the key is TS's own spelling — <c>"strictNullChecks"</c>, not <c>"StrictNullChecks"</c> — because
    /// that is what TS's call sites pass and what a caller of this port will pass. TS types the parameter by the
    /// eight-member union <c>StrictOptionName</c> and reads <c>compilerOptions[flag]</c> through
    /// <see cref="ICompilerOptions"/>'s index signature; the port takes the key as a <see cref="string"/> and switches
    /// on it, because a C# interface's named slots cannot be indexed by their TS key. (The index signature is not
    /// ported for the same reason: a JS object is one dictionary, so <c>options[name]</c> and <c>options.name</c> are
    /// the same storage, whereas a class or interface can only reconcile the two with a switch that has to be kept in
    /// step with every slot. Every dynamic read of it in the reference goes through a <c>CommandLineOption</c>
    /// descriptor — <c>getCompilerOptionValue</c> at utilities.ts:9399-9403, program.ts:1525, builder.ts:1449 — and
    /// <c>CommandLineOption</c> is M8's, so that key→value dispatch lands with it.) The eight arms below are the
    /// union's members in its declared order, and the member each one reads is the same-named slot in PascalCase.
    /// <para>
    /// PORT-NOTE: the body is TS's ternary, <c>compilerOptions[flag] === undefined ? (compilerOptions.strict !==
    /// false) : !!compilerOptions[flag]</c>. For a <c>bool?</c> slot that is exactly <c>x ?? (Strict != false)</c>:
    /// the slot having no value is the <c>undefined</c> case, <c>true</c> is truthy and <c>false</c> is not. TS does
    /// distinguish <c>undefined</c> from <c>null</c> in that test — a null-valued key would answer <c>false</c>
    /// instead of falling back to <c>strict</c> — but no options object the reference can build holds a null there:
    /// <c>validateJsonOptionValue</c> returns <c>undefined</c> for a null value (commandLineParser.ts:3853, reached from
    /// <c>convertJsonOption</c> at 3827), checked with
    /// <c>lib/typescript.js</c>, where <c>convertCompilerOptionsFromJson({ strict: true, strictNullChecks: null })</c>
    /// leaves that slot's value <c>undefined</c> and reports no diagnostic, and a key that was never specified is
    /// <c>undefined</c> as well. §5.1's single <c>null</c> is therefore faithful for every reachable input.
    /// </para>
    /// <para>
    /// PORT-NOTE: the keys TS passes are exactly these eight. <c>grep -rn "getStrictOptionValue(" src/compiler</c>
    /// gives 23 call sites, and every one names a member of the union: checker.ts:1535-1542 (all eight in a row),
    /// checker.ts:33086, program.ts:4076, 4079 and 5167, expressionToTypeNode.ts:178, transformers/typeSerializer.ts:141,
    /// and utilities.ts:9231-9280 (the eight strict-family entries of the computed table, all defined in terms of this
    /// function). The twenty-third is the dynamic <c>option.name as StrictOptionName</c> at utilities.ts:9400, where the
    /// descriptor that supplies the name is one of the eight <c>strictFlag: true</c> options declared in
    /// commandLineParser.ts.
    /// </para>
    /// <para>
    /// PORT-NOTE: this batch's contract asked for a <c>default: false</c> arm while also requiring the reference's
    /// behaviour for the keys TS passes. TS has an answer for a key outside the union —
    /// <c>compilerOptions[flag]</c> is <c>undefined</c>, so the read falls through to <c>strict !== false</c>, checked
    /// against <c>lib/typescript.js</c> (<c>getStrictOptionValue({ strict: true }, "bogus")</c> is <c>true</c>,
    /// <c>{ strict: false }</c> is <c>false</c>) — and answering <c>false</c> there would be new behaviour in a case
    /// the reference decides. So the default arm answers what TS answers, and it stays unreachable for the eight names
    /// above. <c>CompilerOptionsTests</c> pins that arm explicitly so the deviation from the literal instruction is
    /// visible rather than accidental.
    /// </para>
    /// </remarks>
    public static bool GetStrictOptionValue(ICompilerOptions? options, string option)
    {
        var compilerOptions = options!;
        return option switch
        {
            "noImplicitAny" => compilerOptions.NoImplicitAny ?? (compilerOptions.Strict != false),
            "noImplicitThis" => compilerOptions.NoImplicitThis ?? (compilerOptions.Strict != false),
            "strictNullChecks" => compilerOptions.StrictNullChecks ?? (compilerOptions.Strict != false),
            "strictFunctionTypes" => compilerOptions.StrictFunctionTypes ?? (compilerOptions.Strict != false),
            "strictBindCallApply" => compilerOptions.StrictBindCallApply ?? (compilerOptions.Strict != false),
            "strictPropertyInitialization" => compilerOptions.StrictPropertyInitialization ?? (compilerOptions.Strict != false),
            "strictBuiltinIteratorReturn" => compilerOptions.StrictBuiltinIteratorReturn ?? (compilerOptions.Strict != false),
            "useUnknownInCatchVariables" => compilerOptions.UseUnknownInCatchVariables ?? (compilerOptions.Strict != false),
            _ => compilerOptions.Strict != false,
        };
    }

    /// <summary>ported from: src/compiler/utilities.ts:9406-9409 — <c>getJSXTransformEnabled</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is a non-null <c>CompilerOptions</c>; every reader in this file takes the nullable
    /// <see cref="ICompilerOptions"/> the port's model stores (see this file's remarks on <c>getEmitScriptTarget</c>).
    /// The <c>jsx</c> slot is a <c>JsxEmit?</c>, so an unset option matches none of the three arms — the same answer as
    /// the reference's <c>undefined</c>.
    /// </remarks>
    public static bool GetJSXTransformEnabled(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        var jsx = compilerOptions.Jsx;
        return jsx == JsxEmit.React || jsx == JsxEmit.ReactJSX || jsx == JsxEmit.ReactJSXDev;
    }
}
