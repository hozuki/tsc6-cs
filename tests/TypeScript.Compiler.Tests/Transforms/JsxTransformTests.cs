namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/jsx.ts:92-681
/// <summary>
/// Acceptance for the JSX transform (M7-B): the classic and automatic runtimes, the children prop, the <c>key</c>
/// handling, the spread rewrites, the JSX text re-flow and entities, and the tag/attribute name spellings.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with
/// <c>ts.transpileModule(source, { compilerOptions, fileName: "/a.tsx" })</c> from the reference's own
/// <c>lib/typescript.js</c> (6.0.3), at the fixture's own <c>jsx*</c> options plus <c>target</c>/<c>module</c> as the
/// fixture names them, <c>module: ESNext</c> and <c>alwaysStrict: false</c>. The measurements were taken with the
/// <c>declare</c> prologue each fixture's source carries (which the ts transform erases), and with the root file named
/// <c>/a.tsx</c> — the name <c>jsx: react-jsxdev</c> writes into its <c>_jsxFileName</c> literal. None of the
/// expectations was derived from this port's own output.
/// </para>
/// <para>
/// PORT-NOTE (the chain): this fixture drives <c>TypeScriptTransform.TransformTypeScript</c> and then
/// <c>JsxTransform.TransformJsx</c>, the two transformers the reference's <c>getScriptTransformers</c> places at
/// transformer.ts:133-144 (the JSX transform is gated by <c>getJSXTransformEnabled</c>, which
/// <see cref="Preserve_JsxSurvivesBecauseTheGateExcludesTheTransform"/> pins). The ES5 fixture adds
/// <c>ES2015Transform.TransformES2015</c>, which follows the JSX transform there, because the reference's ES5 chain
/// rewrites the fixture's <c>const</c> to <c>var</c>. The module transformer the reference appends last is not in the
/// port's chain yet (it is another M7-B slice), and every fixture here is a module whose emit that transformer leaves
/// alone — measured, not assumed: the JSX transform's inserted <c>import</c> statements survive it verbatim.
/// </para>
/// <para>
/// PORT-NOTE (<c>alwaysStrict: false</c>): as in <c>TypeScriptTransformTests</c> and <c>ES2015TransformTests</c>, the
/// prologue is disabled so each expectation names only the JSX rewrite under test.
/// </para>
/// </remarks>
public class JsxTransformTests
{
    /// <summary>The smallest library that lets <c>initializeTypeChecker</c> resolve the globals it asks for.</summary>
    private static readonly string[] StubLib =
    [
        "interface Array<T> { }",
        "interface Object { }",
        "interface Function { }",
        "declare var undefined: undefined;",
    ];

    /// <summary>Builds the program the test's text is checked in and asks its checker for the emit resolver.</summary>
    private static (Program Program, SourceFile Root, FakeCompilerOptions Options, IEmitResolver Resolver) CreateProgram(
        string text,
        FakeCompilerOptions options)
    {
        var system = new FakeSystem();
        system.Files["/a.tsx"] = text;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        system.Files[host.GetDefaultLibFileName(options)] = string.Join("\n", StubLib) + "\n";
        var program = Program.CreateProgram(["/a.tsx"], options, host);
        var root = program.GetSourceFiles().Single(file => (file.FileName ?? "").EndsWith("a.tsx", StringComparison.Ordinal));
        var resolver = program.GetTypeChecker().GetEmitResolver(root, /*cancellationToken*/ null, /*forceDts*/ false);
        return (program, root, options, resolver);
    }

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's own transformer order.</summary>
    /// <param name="text">The source text, written to <c>/a.tsx</c>.</param>
    /// <param name="options">The fixture's own compiler options.</param>
    /// <param name="runJsxTransform">Whether the JSX transform is in the chain (the reference's gate decides that).</param>
    /// <param name="transformChain">Whether to append the ES2015 transform (the reference's ES5 chain has it).</param>
    private static string Emit(
        string text,
        FakeCompilerOptions options,
        bool runJsxTransform = true,
        bool transformChain = false)
    {
        var (program, root, _, resolver) = CreateProgram(text, options);

        var emitted = "";
        var emitHost = program.GetEmitHost((fileName, writeText, _, _, _, _, _) =>
        {
            // PORT-NOTE: `jsx: preserve` keeps the `.jsx` output extension (emitter.ts:188-190), so the callback
            // accepts both spellings; the fixtures that transform JSX emit `.js`.
            if (fileName.EndsWith(".js", StringComparison.Ordinal) || fileName.EndsWith(".jsx", StringComparison.Ordinal))
            {
                emitted = writeText;
            }
        });

        List<Func<ITransformerContext, Func<object?, object?>>> scriptTransformers = [TypeScriptTransform.TransformTypeScript];
        if (runJsxTransform)
        {
            scriptTransformers.Add(JsxTransform.TransformJsx);
        }

        if (transformChain)
        {
            scriptTransformers.Add(ES2015Transform.TransformES2015);
        }

        Emitter.EmitFiles(
            resolver,
            emitHost,
            root,
            new EmitTransformers
            {
                ScriptTransformers = scriptTransformers,
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    /// <summary>The compiler options a fixture's measurement used.</summary>
    /// <remarks>PORT-NOTE: <c>target</c> defaults to <c>ESNext</c> so the measurement's chain holds no downlevel
    /// transform; the one fixture that needs <c>ES5</c> names it.</remarks>
    private static FakeCompilerOptions Options(
        JsxEmit jsx,
        ScriptTarget target = ScriptTarget.ESNext,
        string? jsxFactory = null,
        string? jsxFragmentFactory = null,
        string? jsxImportSource = null,
        string? reactNamespace = null)
        => new()
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            Target = target,
            Module = ModuleKind.ESNext,
            Jsx = jsx,
            JsxFactory = jsxFactory,
            JsxFragmentFactory = jsxFragmentFactory,
            JsxImportSource = jsxImportSource,
            ReactNamespace = reactNamespace,
        };

    /// <summary>Asserts the emitted text against a raw literal, normalizing the literal's line endings.</summary>
    /// <remarks>PORT-NOTE: a multi-line raw literal does not carry the newline that would follow its last line, while
    /// <see cref="Emit"/> answers the writer's text including it; the helper states that one newline so every
    /// expectation can stay a plain copy of the reference's output (the shape <c>ES2015TransformTests</c> settled).</remarks>
    private static void AssertEmitted(string expected, string actual)
        => Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", actual);

    // ---------------------------------------------------------------------------------------------
    // The fixtures (each source is the measured one, including the `declare`s it starts with)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The source text of the <c>AutomaticRuntime_SelfClosingElement</c> fixture.</summary>
    private const string AutomaticRuntime_SelfClosingElement = "declare var React: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_StaticChildren</c> fixture.</summary>
    private const string AutomaticRuntime_StaticChildren = "declare var React: any;\nconst el = <div a=\"b\"><span/><span/></div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_NestedElement</c> fixture.</summary>
    private const string AutomaticRuntime_NestedElement = "declare var React: any;\nconst el = <div><span>text</span></div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_FragmentWithChildren</c> fixture.</summary>
    private const string AutomaticRuntime_FragmentWithChildren = "declare var React: any;\nconst el = <><span/><span/></>;";

    /// <summary>The source text of the <c>AutomaticRuntime_EmptyFragment</c> fixture.</summary>
    private const string AutomaticRuntime_EmptyFragment = "declare var React: any;\nconst el = <></>;";

    /// <summary>The source text of the <c>AutomaticRuntime_NoChildrenAttribute</c> fixture.</summary>
    private const string AutomaticRuntime_NoChildrenAttribute = "declare var React: any;\nconst el = <div></div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_KeyAttributeComeBeforeSpread</c> fixture.</summary>
    private const string AutomaticRuntime_KeyAttributeComeBeforeSpread = "declare var React: any;\ndeclare const s: any;\nconst el = <div key=\"k\" {...s}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_KeyAttributeAfterPropsSpreadFallsBackToCreateElement</c> fixture.</summary>
    private const string AutomaticRuntime_KeyAttributeAfterPropsSpreadFallsBackToCreateElement = "declare var React: any;\ndeclare const s: any;\nconst el = <div {...s} key=\"k\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_SpreadAtEs5RequestsTheAssignHelper</c> fixture.</summary>
    private const string AutomaticRuntime_SpreadAtEs5RequestsTheAssignHelper = "declare var React: any;\ndeclare const s: any;\nconst el = <div a=\"b\" {...s} c=\"d\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ChildSpreadIsStatic</c> fixture.</summary>
    private const string AutomaticRuntime_ChildSpreadIsStatic = "declare var React: any;\ndeclare const c: any[];\nconst el = <div>{...c}</div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ElementValuedAttribute</c> fixture.</summary>
    private const string AutomaticRuntime_ElementValuedAttribute = "declare var React: any;\nconst el = <div a={<span/>}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_BooleanAndEmptyExpressionAttributes</c> fixture.</summary>
    private const string AutomaticRuntime_BooleanAndEmptyExpressionAttributes = "declare var React: any;\nconst el = <div a b={}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ImportSourceOption</c> fixture.</summary>
    private const string AutomaticRuntime_ImportSourceOption = "declare var React: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_NamespacedTagName</c> fixture.</summary>
    private const string AutomaticRuntime_NamespacedTagName = "declare var React: any;\nconst el = <a:b c=\"d\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_AttributeNameForms</c> fixture.</summary>
    private const string AutomaticRuntime_AttributeNameForms = "declare var React: any;\nconst el = <div a:b=\"c\" a-b=\"d\" data-x=\"e\" A_1=\"f\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_TextReflowAndEntities</c> fixture.</summary>
    private const string AutomaticRuntime_TextReflowAndEntities = "declare var React: any;\nconst el = <div>\n  a&amp;b\n  &#65;&#x42;&nbsp;\n</div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_InlineSpreadObjectIsFlattened</c> fixture.</summary>
    private const string AutomaticRuntime_InlineSpreadObjectIsFlattened = "declare var React: any;\nconst el = <div {...{ a: 1 }}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_InlineSpreadObjectWithSpreadIsFlattened</c> fixture.</summary>
    private const string AutomaticRuntime_InlineSpreadObjectWithSpreadIsFlattened = "declare var React: any;\ndeclare const a: any;\nconst el = <div {...{ ...a }}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ProtoPropertyStopsTheInlineSpread</c> fixture.</summary>
    private const string AutomaticRuntime_ProtoPropertyStopsTheInlineSpread = "declare var React: any;\nconst el = <div {...{ [\"__proto__\"]: 1 }}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_SelfClosingChild</c> fixture.</summary>
    private const string AutomaticRuntime_SelfClosingChild = "declare var React: any;\nconst el = <div><br/></div>;";

    /// <summary>The source text of the <c>DevelopmentRuntime_FilenameAndSource</c> fixture.</summary>
    private const string DevelopmentRuntime_FilenameAndSource = "declare var React: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The source text of the <c>DevelopmentRuntime_KeyArgument</c> fixture.</summary>
    private const string DevelopmentRuntime_KeyArgument = "declare var React: any;\nconst el = <div key=\"k\" a=\"b\"/>;";

    /// <summary>The source text of the <c>DevelopmentRuntime_FragmentWithChildren</c> fixture.</summary>
    private const string DevelopmentRuntime_FragmentWithChildren = "declare var React: any;\nconst el = <><div/></>;";

    /// <summary>The source text of the <c>Classic_DefaultFactory</c> fixture.</summary>
    private const string Classic_DefaultFactory = "declare var React: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The source text of the <c>Classic_JsxFactoryOption</c> fixture.</summary>
    private const string Classic_JsxFactoryOption = "declare var h: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The source text of the <c>Classic_ReactNamespaceOption</c> fixture.</summary>
    private const string Classic_ReactNamespaceOption = "declare var R: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The source text of the <c>Classic_FragmentFactories</c> fixture.</summary>
    private const string Classic_FragmentFactories = "declare var h: any;\ndeclare var hf: any;\nconst el = <><div/></>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ExpressionChildBecomesTheChildrenProp</c> fixture.</summary>
    private const string AutomaticRuntime_ExpressionChildBecomesTheChildrenProp = "declare var React: any;\ndeclare const c: any;\nconst el = <div>{c}</div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_TextAndExpressionChildrenMakeTheCallStatic</c> fixture.</summary>
    private const string AutomaticRuntime_TextAndExpressionChildrenMakeTheCallStatic = "declare var React: any;\ndeclare const c: any;\nconst el = <div>a{c}b</div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_MultiLineTextTrimsEveryLine</c> fixture.</summary>
    private const string AutomaticRuntime_MultiLineTextTrimsEveryLine = "declare var React: any;\nconst el = <div>\n  hi \n</div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_SingleLineTextKeepsItsTrailingWhitespace</c> fixture.</summary>
    private const string AutomaticRuntime_SingleLineTextKeepsItsTrailingWhitespace = "declare var React: any;\nconst el = <div>hi </div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_TextEntityWithAnUnknownNameIsLeftAlone</c> fixture.</summary>
    private const string AutomaticRuntime_TextEntityWithAnUnknownNameIsLeftAlone = "declare var React: any;\nconst el = <div>&notanentity;</div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_SingleQuotedAttributeValueStaysSingleQuoted</c> fixture.</summary>
    private const string AutomaticRuntime_SingleQuotedAttributeValueStaysSingleQuoted = "declare var React: any;\nconst el = <div a='b'/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_AttributeEntitiesAreDecodedAndTheValueReQuoted</c> fixture.</summary>
    private const string AutomaticRuntime_AttributeEntitiesAreDecodedAndTheValueReQuoted = "declare var React: any;\nconst el = <div a=\"&quot;x&quot;\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_AttributeStringEscapesAreReEscaped</c> fixture.</summary>
    private const string AutomaticRuntime_AttributeStringEscapesAreReEscaped = "declare var React: any;\nconst el = <div a=\"a\\nb\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ImportSourcePragmaNamesTheRuntimeModule</c> fixture.</summary>
    private const string AutomaticRuntime_ImportSourcePragmaNamesTheRuntimeModule = "/** @jsxImportSource preact */\nconst el = <div/>;";

    /// <summary>The source text of the <c>DevelopmentRuntime_StaticChildrenFlag</c> fixture.</summary>
    private const string DevelopmentRuntime_StaticChildrenFlag = "declare var React: any;\nconst el = <div><span/><span/></div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_FragmentChildBecomesTheChildrenPropValue</c> fixture.</summary>
    private const string AutomaticRuntime_FragmentChildBecomesTheChildrenPropValue = "declare var React: any;\nconst el = <div><></></div>;";

    /// <summary>The source text of the <c>AutomaticRuntime_InlineSpreadObjectBeforeKeyIsNotAPropsSpread</c> fixture.</summary>
    private const string AutomaticRuntime_InlineSpreadObjectBeforeKeyIsNotAPropsSpread = "declare var React: any;\nconst el = <div {...{ a: 1 }} key=\"k\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_InlineSpreadWithSpreadBeforeKeyDoesFallBack</c> fixture.</summary>
    private const string AutomaticRuntime_InlineSpreadWithSpreadBeforeKeyDoesFallBack = "declare var React: any;\ndeclare const a: any;\nconst el = <div {...{ ...a }} key=\"k\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_AttributeSpanOrderIsPreserved</c> fixture.</summary>
    private const string AutomaticRuntime_AttributeSpanOrderIsPreserved = "declare var React: any;\ndeclare const s: any;\nconst el = <div a=\"1\" {...s} b=\"3\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_EmptyExpressionChildIsDropped</c> fixture.</summary>
    private const string AutomaticRuntime_EmptyExpressionChildIsDropped = "declare var React: any;\nconst el = <div>{}</div>;";

    /// <summary>The source text of the <c>Classic_FragmentWithTwoChildren_PutsEachChildOnItsOwnLine</c> fixture.</summary>
    private const string Classic_FragmentWithTwoChildren_PutsEachChildOnItsOwnLine = "declare var React: any;\nconst el = <><div/><div/></>;";

    /// <summary>The source text of the <c>Classic_SpreadFirstAtEs5_CopiesBeforeTheSpread</c> fixture.</summary>
    private const string Classic_SpreadFirstAtEs5_CopiesBeforeTheSpread = "declare var React: any;\ndeclare const s: any;\nconst el = <div {...s} a=\"b\"/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_ElementAndFragmentAttributeValues</c> fixture.</summary>
    private const string AutomaticRuntime_ElementAndFragmentAttributeValues = "declare var React: any;\nconst el = <div a={<br/>} b={<></>}/>;";

    /// <summary>The source text of the <c>AutomaticRuntime_MemberExpressionTagName</c> fixture.</summary>
    private const string AutomaticRuntime_MemberExpressionTagName = "declare var React: any;\ndeclare const M: any;\nconst el = <M.D a=\"b\"/>;";

    /// <summary>The source text of the <c>Classic_InlineSpreadObjectAtEs5NeedsNoHelper</c> fixture.</summary>
    private const string Classic_InlineSpreadObjectAtEs5NeedsNoHelper = "declare var React: any;\nconst el = <div a=\"b\" {...{ c: 1 }}/>;";

    /// <summary>The source text of the <c>Preserve</c> fixture.</summary>
    private const string Preserve_OutputIsUntouched = "declare var React: any;\nconst el = <div a=\"b\"/>;";

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_SelfClosingElement</c>.</summary>
    /// <remarks>A self-closing element with one attribute is a <c>jsx</c> call, and the runtime import the transform inserted names the helper <c>_jsx</c> as the local.</remarks>
    [Fact]
    public void SelfClosingElement_UsesTheAutomaticRuntimeJsxImport()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: "b" });
            """,
            Emit(AutomaticRuntime_SelfClosingElement, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_StaticChildren</c>.</summary>
    /// <remarks>Two non-whitespace children make the call <c>jsxs</c> and move the semantic children into a <c>children</c> array prop; both runtime names are imported once, in the order the transform first asked for them.</remarks>
    [Fact]
    public void TwoElementChildren_MakeTheCallStatic()
        => AssertEmitted(
            """
            import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
            const el = _jsxs("div", { a: "b", children: [_jsx("span", {}), _jsx("span", {})] });
            """,
            Emit(AutomaticRuntime_StaticChildren, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_NestedElement</c>.</summary>
    /// <remarks>The nested element is transformed before its parent, so it is the <c>children</c> prop value; a text child becomes a string prop on the inner call.</remarks>
    [Fact]
    public void NestedElement_BecomesTheChildrenPropValue()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: _jsx("span", { children: "text" }) });
            """,
            Emit(AutomaticRuntime_NestedElement, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_FragmentWithChildren</c>.</summary>
    /// <remarks>A fragment tag is the imported <c>Fragment</c> binding, and the import order is the order the transform registered the names: the children are visited first (<c>jsx</c>), then the fragment reference, then the parent callee (<c>jsxs</c>).</remarks>
    [Fact]
    public void Fragment_ImportsTheRuntimeFragment()
        => AssertEmitted(
            """
            import { jsx as _jsx, Fragment as _Fragment, jsxs as _jsxs } from "react/jsx-runtime";
            const el = _jsxs(_Fragment, { children: [_jsx("span", {}), _jsx("span", {})] });
            """,
            Emit(AutomaticRuntime_FragmentWithChildren, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_EmptyFragment</c>.</summary>
    /// <remarks>A fragment with no children has no static children, so it is <c>jsx</c> — and the fragment reference is registered first, before the callee, which is why the import lists <c>Fragment</c> first here.</remarks>
    [Fact]
    public void EmptyFragment_IsNotStatic()
        => AssertEmitted(
            """
            import { Fragment as _Fragment, jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx(_Fragment, {});
            """,
            Emit(AutomaticRuntime_EmptyFragment, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_NoChildrenAttribute</c>.</summary>
    /// <remarks>React wants <c>{}</c> rather than <c>null</c> for an element with no attributes.</remarks>
    [Fact]
    public void NoAttributes_EmitsAnEmptyPropsObject()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", {});
            """,
            Emit(AutomaticRuntime_NoChildrenAttribute, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_KeyAttributeComeBeforeSpread</c>.</summary>
    /// <remarks>A <c>key</c> that does not come after a props spread is lifted out of the props object and passed as the third argument.</remarks>
    [Fact]
    public void KeyBeforeSpread_StaysOnTheAutomaticPath()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { ...s }, "k");
            """,
            Emit(AutomaticRuntime_KeyAttributeComeBeforeSpread, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_KeyAttributeAfterPropsSpreadFallsBackToCreateElement</c>.</summary>
    /// <remarks>The automatic runtime cannot express a <c>key</c> that follows a spread, so the transform falls back to <c>createElement</c>, imported from the runtime import base (<c>react</c>) rather than the jsx-runtime, and the key stays a plain prop.</remarks>
    [Fact]
    public void KeyAfterPropsSpread_FallsBackToCreateElement()
        => AssertEmitted(
            """
            import { createElement as _createElement } from "react";
            const el = _createElement("div", { ...s, key: "k" });
            """,
            Emit(AutomaticRuntime_KeyAttributeAfterPropsSpreadFallsBackToCreateElement, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_SpreadAtEs5RequestsTheAssignHelper</c>.</summary>
    /// <remarks>Below ES2018 the props object cannot hold a spread, so the attributes become <c>__assign(...)</c> segments and the transform attaches the <c>__assign</c> emit helper to the file — which the printer emits above the inserted import. The chain adds <c>ES2015Transform.TransformES2015</c> because the reference's own ES5 chain does (<c>const</c> becomes <c>var</c>).</remarks>
    [Fact]
    public void SpreadAtEs5_RequestsTheAssignHelper()
        => AssertEmitted(
            """
            var __assign = (this && this.__assign) || function () {
                __assign = Object.assign || function(t) {
                    for (var s, i = 1, n = arguments.length; i < n; i++) {
                        s = arguments[i];
                        for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                            t[p] = s[p];
                    }
                    return t;
                };
                return __assign.apply(this, arguments);
            };
            import { jsx as _jsx } from "react/jsx-runtime";
            var el = _jsx("div", __assign({ a: "b" }, s, { c: "d" }));
            """,
            Emit(AutomaticRuntime_SpreadAtEs5RequestsTheAssignHelper, Options(JsxEmit.ReactJSX, target: ScriptTarget.ES5), transformChain: true));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ChildSpreadIsStatic</c>.</summary>
    /// <remarks>A <c>{...children}</c> child is a spread element inside the <c>children</c> array, and its presence alone makes the call static (<c>jsxs</c>).</remarks>
    [Fact]
    public void SpreadChild_IsStatic()
        => AssertEmitted(
            """
            import { jsxs as _jsxs } from "react/jsx-runtime";
            const el = _jsxs("div", { children: [...c] });
            """,
            Emit(AutomaticRuntime_ChildSpreadIsStatic, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ElementValuedAttribute</c>.</summary>
    /// <remarks>An attribute whose value is an element is its own <c>jsx</c> call at <c>isChild: false</c>, so it is not put on a new line.</remarks>
    [Fact]
    public void ElementValuedAttribute_IsTransformedInPlace()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: _jsx("span", {}) });
            """,
            Emit(AutomaticRuntime_ElementValuedAttribute, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_BooleanAndEmptyExpressionAttributes</c>.</summary>
    /// <remarks>A shorthand attribute and an attribute whose value is the empty JSX expression both become <c>true</c>.</remarks>
    [Fact]
    public void BooleanAndEmptyExpressionAttributes_AreTrue()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: true, b: true });
            """,
            Emit(AutomaticRuntime_BooleanAndEmptyExpressionAttributes, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ImportSourceOption</c>.</summary>
    /// <remarks><c>jsxImportSource</c> replaces <c>react</c> as the base of the runtime import the transform inserts.</remarks>
    [Fact]
    public void ImportSourceOption_NamesTheRuntimeModule()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "preact/jsx-runtime";
            const el = _jsx("div", { a: "b" });
            """,
            Emit(AutomaticRuntime_ImportSourceOption, Options(JsxEmit.ReactJSX, jsxImportSource: "preact")));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_NamespacedTagName</c>.</summary>
    /// <remarks>A namespaced tag name is the intrinsic string <c>"a:b"</c>, not an entity-name expression.</remarks>
    [Fact]
    public void NamespacedTagName_BecomesAStringTagName()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("a:b", { c: "d" });
            """,
            Emit(AutomaticRuntime_NamespacedTagName, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_AttributeNameForms</c>.</summary>
    /// <remarks>Only an attribute name matching the reference's <c>/^[A-Z_\w*$/i</c> test stays a plain name (<c>A_1</c>); <c>a:b</c>, <c>a-b</c> and <c>data-x</c> are quoted string property names.</remarks>
    [Fact]
    public void AttributeNameForms_AreQuotedWhenTheyAreNotIdentifierShaped()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { "a:b": "c", "a-b": "d", "data-x": "e", A_1: "f" });
            """,
            Emit(AutomaticRuntime_AttributeNameForms, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_TextReflowAndEntities</c>.</summary>
    /// <remarks>Each line is trimmed at both ends and the lines are joined with a single space; <c>&amp;amp;</c>, <c>&amp;#65;</c>, <c>&amp;#x42;</c> (both numeric forms) and <c>&amp;nbsp;</c> decode to <c>&amp;</c>, <c>A</c>, <c>B</c> and U+00A0.</remarks>
    [Fact]
    public void JsxText_IsReflowedAndItsEntitiesDecoded()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: "a&b AB\u00A0" });
            """,
            Emit(AutomaticRuntime_TextReflowAndEntities, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_InlineSpreadObjectIsFlattened</c>.</summary>
    /// <remarks>A spread of an inline object literal is flattened into the props object, so no <c>__assign</c>/<c>Object.assign</c> is needed.</remarks>
    [Fact]
    public void InlineSpreadObject_IsFlattened()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: 1 });
            """,
            Emit(AutomaticRuntime_InlineSpreadObjectIsFlattened, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_InlineSpreadObjectWithSpreadIsFlattened</c>.</summary>
    /// <remarks>An inline object literal that itself contains a spread is still flattened at the first level: its <c>...a</c> becomes a spread assignment in the props object.</remarks>
    [Fact]
    public void InlineSpreadObjectWithSpread_KeepsTheSpreadAssignment()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { ...a });
            """,
            Emit(AutomaticRuntime_InlineSpreadObjectWithSpreadIsFlattened, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ProtoPropertyStopsTheInlineSpread</c>.</summary>
    /// <remarks><c>hasProto</c> is true here, so the object literal is left alone; the test reads a literal name only, so the computed form survives as a computed key rather than being lifted.</remarks>
    [Fact]
    public void ProtoPropertyInSpreadObject_StopsTheFlattening()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { ["__proto__"]: 1 });
            """,
            Emit(AutomaticRuntime_ProtoPropertyStopsTheInlineSpread, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_SelfClosingChild</c>.</summary>
    /// <remarks>A self-closing child element is a <c>jsx</c> call in the <c>children</c> prop.</remarks>
    [Fact]
    public void SelfClosingChild_BecomesTheChildrenPropValue()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: _jsx("br", {}) });
            """,
            Emit(AutomaticRuntime_SelfClosingChild, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>DevelopmentRuntime_FilenameAndSource</c>.</summary>
    /// <remarks><c>jsxDEV</c> takes the key (here <c>void 0</c>), the static-children flag, the <c>__source</c> object (a lazily-created file-level <c>_jsxFileName</c> const, plus the 1-based line/column of the element after leading trivia) and <c>this</c>. The source line is 2 because the fixture's own <c>declare</c> statement precedes the element.</remarks>
    [Fact]
    public void JsxDev_AddsTheFilenameAndSourceObjectArguments()
        => AssertEmitted(
            """
            import { jsxDEV as _jsxDEV } from "react/jsx-dev-runtime";
            const _jsxFileName = "/a.tsx";
            const el = _jsxDEV("div", { a: "b" }, void 0, false, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 12 }, this);
            """,
            Emit(DevelopmentRuntime_FilenameAndSource, Options(JsxEmit.ReactJSXDev)));

    /// <summary>The reference emit of the fixture <c>DevelopmentRuntime_KeyArgument</c>.</summary>
    /// <remarks>With a <c>key</c> attribute, the <c>jsxDEV</c> key argument is the key initializer rather than <c>void 0</c>, and the key is still lifted out of the props.</remarks>
    [Fact]
    public void JsxDev_ReplacesVoidZeroWithTheKey()
        => AssertEmitted(
            """
            import { jsxDEV as _jsxDEV } from "react/jsx-dev-runtime";
            const _jsxFileName = "/a.tsx";
            const el = _jsxDEV("div", { a: "b" }, "k", false, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 12 }, this);
            """,
            Emit(DevelopmentRuntime_KeyArgument, Options(JsxEmit.ReactJSXDev)));

    /// <summary>The reference emit of the fixture <c>DevelopmentRuntime_FragmentWithChildren</c>.</summary>
    /// <remarks>Both the fragment and its child element get the development arguments, each with its own source position; the fragment import is registered after its children, as in the production fixture.</remarks>
    [Fact]
    public void JsxDev_TransformsNestedElementsToo()
        => AssertEmitted(
            """
            import { jsxDEV as _jsxDEV, Fragment as _Fragment } from "react/jsx-dev-runtime";
            const _jsxFileName = "/a.tsx";
            const el = _jsxDEV(_Fragment, { children: _jsxDEV("div", {}, void 0, false, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 14 }, this) }, void 0, false, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 12 }, this);
            """,
            Emit(DevelopmentRuntime_FragmentWithChildren, Options(JsxEmit.ReactJSXDev)));

    /// <summary>The reference emit of the fixture <c>Classic_DefaultFactory</c>.</summary>
    /// <remarks>With no factory in scope the classic runtime is <c>React.createElement</c>, and nothing is imported for it.</remarks>
    [Fact]
    public void Classic_Runtime_UsesReactCreateElement()
        => AssertEmitted(
            "const el = React.createElement(\"div\", { a: \"b\" });",
            Emit(Classic_DefaultFactory, Options(JsxEmit.React)));

    /// <summary>The reference emit of the fixture <c>Classic_JsxFactoryOption</c>.</summary>
    /// <remarks><c>jsxFactory</c> names the callee directly (<c>h</c>), because the resolver answers the option as an entity name.</remarks>
    [Fact]
    public void Classic_JsxFactoryOption_NamesTheCallee()
        => AssertEmitted(
            "const el = h(\"div\", { a: \"b\" });",
            Emit(Classic_JsxFactoryOption, Options(JsxEmit.React, jsxFactory: "h")));

    /// <summary>The reference emit of the fixture <c>Classic_ReactNamespaceOption</c>.</summary>
    /// <remarks><c>reactNamespace</c> is the fallback namespace <c>createReactNamespace</c> reads when no factory is in scope, so the callee is <c>R.createElement</c>.</remarks>
    [Fact]
    public void Classic_ReactNamespaceOption_NamesTheNamespace()
        => AssertEmitted(
            "const el = R.createElement(\"div\", { a: \"b\" });",
            Emit(Classic_ReactNamespaceOption, Options(JsxEmit.React, reactNamespace: "R")));

    /// <summary>The reference emit of the fixture <c>Classic_FragmentFactories</c>.</summary>
    /// <remarks>A fragment under the classic runtime calls the fragment factory <c>hf</c> and passes the children as further arguments of the element factory, and a childless element gets the <c>null</c> props argument.</remarks>
    [Fact]
    public void Classic_FragmentFactories_AreBothUsed()
        => AssertEmitted(
            """
            const el = h(hf, null,
                h("div", null));
            """,
            Emit(Classic_FragmentFactories, Options(JsxEmit.React, jsxFactory: "h", jsxFragmentFactory: "hf")));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ExpressionChildBecomesTheChildrenProp</c>.</summary>
    /// <remarks>A single <c>{c}</c> child is a bare expression prop (<c>children: c</c>) rather than a one-element array: that is the one-non-whitespace-child arm of <c>convertJsxChildrenToChildrenPropAssignment</c>.</remarks>
    [Fact]
    public void ExpressionChild_BecomesTheChildrenProp()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: c });
            """,
            Emit(AutomaticRuntime_ExpressionChildBecomesTheChildrenProp, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_TextAndExpressionChildrenMakeTheCallStatic</c>.</summary>
    /// <remarks>Three semantic children (text, expression, text) make the call static and keep their order in the <c>children</c> array — a whitespace-only child does not make it static.</remarks>
    [Fact]
    public void TextAndExpressionChildren_MakeTheCallStatic()
        => AssertEmitted(
            """
            import { jsxs as _jsxs } from "react/jsx-runtime";
            const el = _jsxs("div", { children: ["a", c, "b"] });
            """,
            Emit(AutomaticRuntime_TextAndExpressionChildrenMakeTheCallStatic, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_MultiLineTextTrimsEveryLine</c>.</summary>
    /// <remarks>A line that ends with a line break loses its trailing space too: <c>"hi "</c> between two breaks trims to <c>hi</c>, because only the last line keeps trailing whitespace.</remarks>
    [Fact]
    public void MultiLineText_TrimsEveryLine()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: "hi" });
            """,
            Emit(AutomaticRuntime_MultiLineTextTrimsEveryLine, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_SingleLineTextKeepsItsTrailingWhitespace</c>.</summary>
    /// <remarks>The last line is only trimmed on the left, so a single-line text child keeps its trailing space.</remarks>
    [Fact]
    public void SingleLineText_KeepsItsTrailingWhitespace()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: "hi " });
            """,
            Emit(AutomaticRuntime_SingleLineTextKeepsItsTrailingWhitespace, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_TextEntityWithAnUnknownNameIsLeftAlone</c>.</summary>
    /// <remarks><c>&amp;notanentity;</c> matches the entity pattern but names no entity in the table, so the reference replaces it with itself — the fallback arm of <c>decodeEntities</c>.</remarks>
    [Fact]
    public void TextEntityWithAnUnknownName_IsLeftAlone()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: "&notanentity;" });
            """,
            Emit(AutomaticRuntime_TextEntityWithAnUnknownNameIsLeftAlone, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_SingleQuotedAttributeValueStaysSingleQuoted</c>.</summary>
    /// <remarks>The parsed literal carries no <c>singleQuote</c>, so <c>isStringDoubleQuoted</c> is asked; it answers false for a single-quoted value, and the recreated literal keeps the single quote.</remarks>
    [Fact]
    public void SingleQuotedAttributeValue_StaysSingleQuoted()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: 'b' });
            """,
            Emit(AutomaticRuntime_SingleQuotedAttributeValueStaysSingleQuoted, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_AttributeEntitiesAreDecodedAndTheValueReQuoted</c>.</summary>
    /// <remarks>The attribute string is recreated whole: its entities are decoded first and its original quote character is kept, so <c>&amp;quot;x&amp;quot;</c> becomes the literal <c>"\"x\""</c>.</remarks>
    [Fact]
    public void AttributeEntitiesAreDecodedAndTheValueReQuoted()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: "\"x\"" });
            """,
            Emit(AutomaticRuntime_AttributeEntitiesAreDecodedAndTheValueReQuoted, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_AttributeStringEscapesAreReEscaped</c>.</summary>
    /// <remarks>A backslash escape in the source is part of the literal’s text (it is not decoded), so recreating the literal escapes the backslash: the two characters <c>a\nb</c> print as <c>"a\\nb"</c>.</remarks>
    [Fact]
    public void AttributeStringEscapesAreReEscaped()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: "a\\nb" });
            """,
            Emit(AutomaticRuntime_AttributeStringEscapesAreReEscaped, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ImportSourcePragmaNamesTheRuntimeModule</c>.</summary>
    /// <remarks>The <c>@jsxImportSource</c> pragma is the file-level source of the implicit import base; the pragma comment is a leading comment of the element’s statement, so it prints after the import the transform inserted ahead of it.</remarks>
    [Fact]
    public void ImportSourcePragma_NamesTheRuntimeModule()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "preact/jsx-runtime";
            /** @jsxImportSource preact */
            const el = _jsx("div", {});
            """,
            Emit(AutomaticRuntime_ImportSourcePragmaNamesTheRuntimeModule, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>DevelopmentRuntime_StaticChildrenFlag</c>.</summary>
    /// <remarks>The development flag is the same <c>isStaticChildren</c> answer the callee is chosen from: the outer element has two children (<c>true</c>) and each inner element has none (<c>false</c>).</remarks>
    [Fact]
    public void JsxDev_SetsTheStaticChildrenFlag()
        => AssertEmitted(
            """
            import { jsxDEV as _jsxDEV } from "react/jsx-dev-runtime";
            const _jsxFileName = "/a.tsx";
            const el = _jsxDEV("div", { children: [_jsxDEV("span", {}, void 0, false, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 17 }, this), _jsxDEV("span", {}, void 0, false, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 24 }, this)] }, void 0, true, { fileName: _jsxFileName, lineNumber: 2, columnNumber: 12 }, this);
            """,
            Emit(DevelopmentRuntime_StaticChildrenFlag, Options(JsxEmit.ReactJSXDev)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_FragmentChildBecomesTheChildrenPropValue</c>.</summary>
    /// <remarks>A fragment used as a child is transformed at <c>isChild: true</c> and becomes the <c>children</c> prop value; the <c>Fragment</c> import is registered before the callee of the inner call, exactly as in the top-level fragment fixture.</remarks>
    [Fact]
    public void FragmentChild_BecomesTheChildrenPropValue()
        => AssertEmitted(
            """
            import { Fragment as _Fragment, jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { children: _jsx(_Fragment, {}) });
            """,
            Emit(AutomaticRuntime_FragmentChildBecomesTheChildrenPropValue, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_InlineSpreadObjectBeforeKeyIsNotAPropsSpread</c>.</summary>
    /// <remarks>The key stays on the automatic path because an inline object literal without a spread assignment does not count as a props spread — the sub-arm of <c>hasKeyAfterPropsSpread</c> that reads <c>expression.properties.some(isSpreadAssignment)</c>, and the literal is flattened so the key is lifted out of it.</remarks>
    [Fact]
    public void InlineSpreadObjectBeforeKey_IsNotAPropsSpread()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: 1 }, "k");
            """,
            Emit(AutomaticRuntime_InlineSpreadObjectBeforeKeyIsNotAPropsSpread, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_InlineSpreadWithSpreadBeforeKeyDoesFallBack</c>.</summary>
    /// <remarks>The same shape with a spread <em>inside</em> the inline literal does count as a props spread, so the <c>key</c> that follows it falls back to <c>createElement</c> and stays a prop.</remarks>
    [Fact]
    public void InlineSpreadWithSpreadBeforeKey_DoesFallBack()
        => AssertEmitted(
            """
            import { createElement as _createElement } from "react";
            const el = _createElement("div", { ...a, key: "k" });
            """,
            Emit(AutomaticRuntime_InlineSpreadWithSpreadBeforeKeyDoesFallBack, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_AttributeSpanOrderIsPreserved</c>.</summary>
    /// <remarks>The attributes are grouped into same-kind spans and flattened back in order, so the spread keeps its place between the two named attributes.</remarks>
    [Fact]
    public void AttributeSpanOrder_IsPreserved()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", { a: "1", ...s, b: "3" });
            """,
            Emit(AutomaticRuntime_AttributeSpanOrderIsPreserved, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_EmptyExpressionChildIsDropped</c>.</summary>
    /// <remarks>An empty <c>{}</c> child is not a semantic child, and visiting it answers <c>null</c>, so no <c>children</c> prop is created at all.</remarks>
    [Fact]
    public void EmptyExpressionChild_IsDropped()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx("div", {});
            """,
            Emit(AutomaticRuntime_EmptyExpressionChildIsDropped, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>Classic_FragmentWithTwoChildren_PutsEachChildOnItsOwnLine</c>.</summary>
    /// <remarks>A classic fragment with more than one child passes each transformed child as its own argument and puts each on a new line.</remarks>
    [Fact]
    public void Classic_FragmentWithTwoChildren_PutsEachChildOnItsOwnLineCall()
        => AssertEmitted(
            """
            const el = React.createElement(React.Fragment, null,
                React.createElement("div", null),
                React.createElement("div", null));
            """,
            Emit(Classic_FragmentWithTwoChildren_PutsEachChildOnItsOwnLine, Options(JsxEmit.React)));

    /// <summary>The reference emit of the fixture <c>Classic_SpreadFirstAtEs5_CopiesBeforeTheSpread</c>.</summary>
    /// <remarks>When the first segment of the ES5 <c>__assign</c> chain is a spread, the transform prepends an empty object literal so the first argument is a fresh object the helper can assign into.</remarks>
    [Fact]
    public void SpreadFirstAtEs5_CopiesBeforeTheSpread()
        => AssertEmitted(
            """
            var __assign = (this && this.__assign) || function () {
                __assign = Object.assign || function(t) {
                    for (var s, i = 1, n = arguments.length; i < n; i++) {
                        s = arguments[i];
                        for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                            t[p] = s[p];
                    }
                    return t;
                };
                return __assign.apply(this, arguments);
            };
            import { jsx as _jsx } from "react/jsx-runtime";
            var el = _jsx("div", __assign({}, s, { a: "b" }));
            """,
            Emit(Classic_SpreadFirstAtEs5_CopiesBeforeTheSpread, Options(JsxEmit.ReactJSX, target: ScriptTarget.ES5), transformChain: true));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_ElementAndFragmentAttributeValues</c>.</summary>
    /// <remarks>Both element-valued attribute forms are transformed at <c>isChild: false</c> and in attribute order, so the <c>Fragment</c> import is registered after the <c>jsx</c> one.</remarks>
    [Fact]
    public void ElementAndFragmentAttributeValues_AreBothTransformed()
        => AssertEmitted(
            """
            import { jsx as _jsx, Fragment as _Fragment } from "react/jsx-runtime";
            const el = _jsx("div", { a: _jsx("br", {}), b: _jsx(_Fragment, {}) });
            """,
            Emit(AutomaticRuntime_ElementAndFragmentAttributeValues, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>AutomaticRuntime_MemberExpressionTagName</c>.</summary>
    /// <remarks>A member-expression tag name is not intrinsic, so it goes through <c>createExpressionFromEntityName</c> and stays the property access <c>M.D</c>.</remarks>
    [Fact]
    public void MemberExpressionTagName_BecomesAnExpression()
        => AssertEmitted(
            """
            import { jsx as _jsx } from "react/jsx-runtime";
            const el = _jsx(M.D, { a: "b" });
            """,
            Emit(AutomaticRuntime_MemberExpressionTagName, Options(JsxEmit.ReactJSX)));

    /// <summary>The reference emit of the fixture <c>Classic_InlineSpreadObjectAtEs5NeedsNoHelper</c>.</summary>
    /// <remarks>An inline object literal spread is flattened into the props object even below ES2018, so the only
    /// expression the ES5 path builds is that literal — <c>singleOrUndefined</c> answers it and the assign helper is
    /// never requested.</remarks>
    [Fact]
    public void InlineSpreadObjectAtEs5_NeedsNoHelper()
        => AssertEmitted(
            "import { jsx as _jsx } from \"react/jsx-runtime\";\nvar el = _jsx(\"div\", { a: \"b\", c: 1 });",
            Emit(Classic_InlineSpreadObjectAtEs5NeedsNoHelper, Options(JsxEmit.ReactJSX, target: ScriptTarget.ES5), transformChain: true));

    // ---------------------------------------------------------------------------------------------
    // The two module arms of transformSourceFile the emit fixtures cannot reach
    // ---------------------------------------------------------------------------------------------

    /// <summary>Runs only the JSX transform over a parsed file and prints it.</summary>
    /// <remarks>
    /// PORT-NOTE (the oracle): the two expectations below were measured from the reference's own transformer pipeline
    /// rather than through <c>transpileModule</c>, because the reference's last transformer
    /// (<c>getModuleTransformer</c>, transformer.ts:186) rewrites what the arm under test inserts: the measurement is
    /// <c>ts.createSourceFile("/a.tsx", source, ESNext, /*setParentNodes*/ true, TSX)</c> — with
    /// <c>commonJsModuleIndicator</c> set to the file itself when the fixture asks for it, the shape a Program's module
    /// detection leaves — then <c>ts.transform(file, [ts.transformJsx], { target: ESNext, jsx: react-jsx })</c> and
    /// <c>ts.createPrinter().printFile(…)</c>. This port does the same three steps: the parser, the JSX transform alone
    /// through <c>Transformer.TransformNodes</c>, and the default printer.
    /// </remarks>
    private static string TransformFileOnly(string text, bool commonJsModule)
    {
        var file = Parser.CreateSourceFile("/a.tsx", text, ScriptTarget.ESNext, /*setParentNodes*/ true, ScriptKind.TSX);
        if (commonJsModule)
        {
            file.CommonJsModuleIndicator = file;
        }

        var result = Transformer.TransformNodes<Node>(
            /*resolver*/ null,
            /*host*/ null,
            NodeFactory.Factory,
            Options(JsxEmit.ReactJSX),
            [file],
            [JsxTransform.TransformJsx],
            /*allowDtsFiles*/ false);

        return PrinterFactory.CreatePrinterWithDefaults()
            .PrintFile((SourceFile)result.Transformed![0])
            .Replace("\r\n", "\n");
    }

    /// <summary>A script file is neither an external module nor a CommonJS one, so nothing is inserted.</summary>
    /// <remarks>The reference's third arm is the empty block whose comment asks whether the checker should report an
    /// error; the transformed element still names the runtime binding, which nothing declares.</remarks>
    [Fact]
    public void ScriptFile_GetsNoRuntimeStatement()
        => Assert.Equal(
            "var el = _jsx(\"div\", { a: \"b\" });\n",
            TransformFileOnly("var el = <div a=\"b\"/>;", commonJsModule: false));

    /// <summary>A file with only a <c>commonJsModuleIndicator</c> gets the runtime import as a <c>require</c>.</summary>
    /// <remarks>The binding pattern is built from the remembered import specifiers, whose <c>propertyName</c> is the
    /// runtime name and whose <c>name</c> is the generated local, so the destructuring is <c>{ jsx: _jsx }</c>.</remarks>
    [Fact]
    public void CommonJsModuleFile_GetsARequireStatement()
        => Assert.Equal(
            "const { jsx: _jsx } = require(\"react/jsx-runtime\");\nvar el = _jsx(\"div\", { a: \"b\" });\n",
            TransformFileOnly("var el = <div a=\"b\"/>;", commonJsModule: true));

    /// <summary>Preserve output is untouched: the chain never includes <c>transformJsx</c>.</summary>
    /// <remarks>The reference's <c>getScriptTransformers</c> pushes <c>transformJsx</c> only when
    /// <c>getJSXTransformEnabled(compilerOptions)</c> is true (transformer.ts:142-144), which is never the case for
    /// <c>JsxEmit.Preserve</c>. The measured expectation below is the reference's own <c>transpileModule</c> output for
    /// the fixture with <c>jsx: preserve</c> — the JSX survives because the transform is not in the chain.</remarks>
    [Fact]
    public void Preserve_JsxSurvivesBecauseTheGateExcludesTheTransform()
    {
        Assert.False(Utilities.GetJSXTransformEnabled(Options(JsxEmit.Preserve)));
        Assert.True(Utilities.GetJSXTransformEnabled(Options(JsxEmit.React)));
        Assert.True(Utilities.GetJSXTransformEnabled(Options(JsxEmit.ReactJSX)));
        Assert.True(Utilities.GetJSXTransformEnabled(Options(JsxEmit.ReactJSXDev)));
        Assert.False(Utilities.GetJSXTransformEnabled(Options(JsxEmit.ReactNative)));

        AssertEmitted(
            "const el = <div a=\"b\"/>;",
            Emit(Preserve_OutputIsUntouched, Options(JsxEmit.Preserve), runJsxTransform: false));
    }
}
