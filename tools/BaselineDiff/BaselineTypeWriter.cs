using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// 工具本身的入口类叫 Program（tools/TypeDiff、tools/EmitDiff 同样如此），而生成基线的 program 参数也是
// Program；别名把两者分开。
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// 类型/符号基线生成器：<c>src/harness/typeWriter.ts</c> 的 <c>TypeWriterWalker</c>（连同它打印类型节点所用的
/// 合成节点下划线打印机），加上 <c>doTypeAndSymbolBaseline</c> 的 <c>generateBaseLine</c>/<c>iterateBaseLine</c>
/// （<c>src/harness/harnessIO.ts:663-862</c>）的移植。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Generate"/> 生成两个基线中的恰好一个（每个测试旁边的 <c>.types</c> 与 <c>.symbols</c>）：同一套
/// walker、同一套行合并、同一套外框（<c>//// [&lt;header&gt;] ////\r\n\r\n</c> 加可选性能块）与同一套
/// <c>\r\n</c> 正文，所以调用方只需用 <c>isSymbolBaseline</c> 为 false 与 true 各调一次。
/// </para>
/// <para>
/// PORT-NOTE：harness 的 <c>checkBaseLines</c> 还给每个单元命名基线文件（<c>checkDuplicatedFileName</c>，
/// <c>harnessIO.ts:859</c>），由 <c>runBaseline</c>/<c>runMultifileBaseline</c> 写盘；那是驱动侧的一半，不影响
/// 文本，所以移植只返回文本，命名留给调用方。
/// </para>
/// <para>
/// PORT-NOTE：正文为空时 <see cref="Generate"/> 返回 <c>null</c> —— 参照实现的
/// <c>return result ? … : null</c>（<c>harnessIO.ts:781</c>），harness 把它当作 <c>&lt;no content&gt;</c>。
/// </para>
/// </remarks>
internal static partial class BaselineTypeWriter
{
    /// <summary>
    /// 生成一个类型或符号基线的完整文本（头行、<c>\r\n\r\n</c>、可选性能块、正文），正文为空时返回 <c>null</c>
    /// —— 与 harness 的 <c>&lt;no content&gt;</c> 对应。
    /// </summary>
    /// <param name="program">已建好的程序；checker 由它自己的 <c>GetTypeChecker()</c> 缓存，所以两次调用共用同一个
    /// checker（参照实现的 <c>fullWalker</c> 也是如此）。</param>
    /// <param name="header">harness 传给 <c>doTypeAndSymbolBaseline</c> 的 <c>header</c>，工作区相对路径。</param>
    /// <param name="allFiles">要遍历的单元，按 harness 的顺序；<c>UnitName</c> 同时是节头与 <c>program.getSourceFile</c>
    /// 的查名字。</param>
    /// <param name="hasErrorBaseline"><c>!!length(result.diagnostics)</c>：有诊断时 <c>any</c> 不再打印成
    /// <c>intrinsicName</c>。</param>
    /// <param name="isSymbolBaseline">false 生成 <c>.types</c>，true 生成 <c>.symbols</c>。</param>
    /// <summary>ported from: src/harness/harnessIO.ts:741-786 — <c>generateBaseLine</c>（性能块），
    /// 816-861 — <c>iterateBaseLine</c>（正文），781 — 外框。</summary>
    internal static string? Generate(
        PortProgram program,
        string header,
        IReadOnlyList<(string UnitName, string Content)> allFiles,
        bool hasErrorBaseline,
        bool isSymbolBaseline,
        bool underlines = true)
    {
        var fullWalker = new TypeWriterWalker(program, hadErrorBaseline: hasErrorBaseline);

        var result = new StringBuilder();
        var perfLines = new List<string>();
        var prePerformanceValues = GetPerformanceBaselineValues(program);
        foreach (var content in IterateBaseLine(fullWalker, allFiles, isSymbolBaseline, underlines))
        {
            result.Append(content);
        }

        var postPerformanceValues = GetPerformanceBaselineValues(program);

        if (!isSymbolBaseline)
        {
            // ported from: src/harness/harnessIO.ts:753-760 — 七项计数与各自的阈值。
            var perfStats = new List<(string Name, int ReportThreshold, int BeforeValue, int AfterValue)>
            {
                ("Strict subtype cache", 1000, prePerformanceValues.StrictSubtype, postPerformanceValues.StrictSubtype),
                ("Subtype cache", 1000, prePerformanceValues.Subtype, postPerformanceValues.Subtype),
                ("Identity cache", 1000, prePerformanceValues.Identity, postPerformanceValues.Identity),
                ("Assignability cache", 1000, prePerformanceValues.Assignable, postPerformanceValues.Assignable),
                ("Type Count", 1000, prePerformanceValues.TypeCount, postPerformanceValues.TypeCount),
                ("Instantiation count", 1500, prePerformanceValues.Instantiation, postPerformanceValues.Instantiation),
                ("Symbol count", 45000, prePerformanceValues.Symbol, postPerformanceValues.Symbol),
            };

            // ported from: src/harness/harnessIO.ts:762-778 — 只有 *后* 值达到阈值的项才打印（圆整后相等则不带
            // ` -> `）。
            if (perfStats.Any(stat => stat.AfterValue >= stat.ReportThreshold))
            {
                perfLines.Add("=== Performance Stats ===");
                foreach (var (name, threshold, preValue, postValue) in perfStats)
                {
                    if (postValue >= threshold)
                    {
                        var preString = ValueToString(preValue);
                        var postString = ValueToString(postValue);
                        if (preString == postString)
                        {
                            perfLines.Add($"{name}: {preString}");
                        }
                        else
                        {
                            perfLines.Add($"{name}: {preString} -> {postString}");
                        }
                    }
                }

                perfLines.Add("");
                perfLines.Add("");
            }
        }

        return result.Length != 0 ? $"//// [{header}] ////\r\n\r\n{string.Join("\n", perfLines)}{result}" : null;
    }

    /// <summary>ported from: src/harness/harnessIO.ts:816-861 — <c>iterateBaseLine</c>，逐单元产出正文。</summary>
    /// <remarks>
    /// PORT-NOTE：<c>skipBaseline</c> 参数（817-819）是 harness 的 <c>noTypesAndSymbols</c> 开关，本移植的 API 没有
    /// 对应槽位：驱动不调用就等于不生成。
    /// </remarks>
    private static IEnumerable<string> IterateBaseLine(
        TypeWriterWalker walker,
        IReadOnlyList<(string UnitName, string Content)> allFiles,
        bool isSymbolBaseline,
        bool underlines)
    {
        foreach (var (unitName, content) in allFiles)
        {
            var typeLines = new StringBuilder("=== " + unitName + " ===\r\n");
            var codeLines = SplitCodeLines(content);
            var gen = isSymbolBaseline ? walker.GetSymbols(unitName) : walker.GetTypes(unitName);
            int? lastIndexWritten = null;
            foreach (var result in gen)
            {
                // 参照实现（829-831）在此**结束整个文件循环**；但 visitNode 只产出非空结果（208-211），符号走查的每
                // 个结果都带 symbol，所以这一支在参照实现里也永远不触发。照抄，语义不变。
                if (isSymbolBaseline && result.Symbol is null)
                {
                    yield break;
                }

                if (lastIndexWritten is null)
                {
                    typeLines.Append(JoinSlice(codeLines, 0, result.Line + 1)).Append("\r\n");
                }
                else if (result.Line != lastIndexWritten)
                {
                    if (!SuppressesBlankLine(codeLines, lastIndexWritten.Value + 1))
                    {
                        typeLines.Append("\r\n");
                    }

                    typeLines.Append(JoinSlice(codeLines, lastIndexWritten.Value + 1, result.Line + 1)).Append("\r\n");
                }

                lastIndexWritten = result.Line;
                var typeOrSymbolString = isSymbolBaseline ? result.Symbol : result.Type;
                var lineText = LineBreakRegExp().Replace(result.SourceText, "");
                var formattedLine = lineText + " : " + typeOrSymbolString;
                typeLines.Append('>').Append(formattedLine).Append("\r\n");
                if (underlines && !string.IsNullOrEmpty(result.Underline))
                {
                    typeLines.Append('>').Append(' ', lineText.Length).Append(" : ").Append(result.Underline).Append("\r\n");
                }
            }

            lastIndexWritten ??= -1;
            if (lastIndexWritten + 1 < codeLines.Count)
            {
                if (!SuppressesBlankLine(codeLines, lastIndexWritten.Value + 1))
                {
                    typeLines.Append("\r\n");
                }

                typeLines.Append(JoinSlice(codeLines, lastIndexWritten.Value + 1, null));
            }

            typeLines.Append("\r\n");
            yield return RemoveTestPathPrefixes(typeLines.ToString());
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 正文的行模型（harnessIO.ts:825-858）
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/harness/harnessIO.ts:825 — <c>ts.flatMap(content.split(/\r?\n/), e =&gt;
    /// e.split(/[\r\u2028\u2029]/))</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：参照实现先按 <c>\r?\n</c> 切、再把每一片按 <c>[\r\u2028\u2029]</c> 切（所以单独的 <c>\r</c>
    /// 不是第一步的分隔符、却是第二步的），两趟的结果与这里一趟等价：<c>\r\n</c> 是一个分隔符、其余三种字符各自
    /// 是分隔符，且每个分隔符都产出空串（<c>"a\r\u2028b"</c> 两趟与一趟都得到 <c>["a", "", "b"]</c>）。
    /// </remarks>
    private static List<string> SplitCodeLines(string content)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                lines.Add(current.ToString());
                current.Clear();
                i++;
            }
            else if (ch is '\r' or '\n' or '\u2028' or '\u2029')
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        lines.Add(current.ToString());
        return lines;
    }

    /// <summary>ported from: src/harness/harnessIO.ts:836/853 — <c>codeLines[i].match(/^\s*[{|}]\s*$/) ||
    /// codeLines[i].trim() === ""</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：JS 的 <c>trim()</c> 还吃掉 U+FEFF，.NET 的 <c>Trim()</c> 不吃；源码行以 BOM 结尾的情形两者会
    /// 不同，除此之外两边一样（<c>\s</c> 都含 Unicode 空白）。
    /// </remarks>
    private static bool SuppressesBlankLine(List<string> codeLines, int index)
        => index < codeLines.Count
            && (BraceOnlyLineRegExp().IsMatch(codeLines[index]) || codeLines[index].Trim().Length == 0);

    /// <summary>ported from: src/harness/harnessIO.ts:833/839/856 — <c>codeLines.slice(…).join("\r\n")</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：<c>Array.prototype.slice</c> 会夹取越界下标，<c>List.GetRange</c> 会抛，而 <c>result.line</c> 来自
    /// 程序里的源文件、<c>codeLines</c> 来自调用方给的单元文本，两者理论上可以不一致；这里保留 slice 的夹取语义。
    /// </remarks>
    private static string JoinSlice(List<string> lines, int from, int? to)
    {
        var start = Math.Max(from, 0);
        var end = Math.Min(to ?? lines.Count, lines.Count);
        return start >= end ? "" : string.Join("\r\n", lines.GetRange(start, end - start));
    }

    /// <summary>ported from: src/harness/util.ts:7-9 — <c>testPathPrefixRegExp</c>、<c>removeTestPathPrefixes</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：<c>retainTrailingDirectorySeparator</c> 在参照实现的两个调用点都不传，所以替换是"去掉前缀"；
    /// <c>file://</c> 形态保留 scheme。
    /// </remarks>
    private static string RemoveTestPathPrefixes(string text)
        => TestPathPrefixRegExp().Replace(text, match => match.Groups[1].Success ? match.Groups[1].Value : "");

    // ---------------------------------------------------------------------------------------------
    // 性能块（harnessIO.ts:791-814）
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/harness/harnessIO.ts:802-814 — <c>getPerformanceBaselineValues</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：<c>getRelationCacheSizes()</c> 返回 <c>object?</c>（<c>ITypeChecker</c> 上没有生成类型），具体类型
    /// 是 <c>Checker.RelationCacheSizes</c>（<c>Checker/Checker.CreateTypeChecker.cs:74</c>），此处按该形状读出四个
    /// 尺寸，不做反射。
    /// </remarks>
    private static PerformanceBaselineValues GetPerformanceBaselineValues(PortProgram program)
    {
        var checker = program.GetTypeChecker();
        var caches = (Checker.RelationCacheSizes)checker.GetRelationCacheSizes()!;
        return new PerformanceBaselineValues(
            caches.StrictSubtype,
            caches.Subtype,
            caches.Identity,
            caches.Assignable,
            checker.GetTypeCount(),
            checker.GetInstantiationCount(),
            checker.GetSymbolCount());
    }

    /// <summary>ported from: src/harness/harnessIO.ts:791-799 — <c>roundToHumanLogarithm</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：JS 的 <c>multipliers.reduce((prev, curr) =&gt; …)</c> 没有初值，等价于从 <c>multipliers[0]</c> 起
    /// 的 <c>Aggregate</c>；<c>Math.log10</c>/<c>Math.pow</c> 与 .NET 的同名函数逐位一致（计数从 10 起，且四个乘数
    /// 与 10 的整数次幂的乘积在常见量级下可精确表示）。
    /// </remarks>
    private static double RoundToHumanLogarithm(double n)
    {
        if (n < 10) return 0;
        var powerOfTen = Math.Floor(Math.Log10(n));
        var basePowerOfTen = Math.Pow(10, powerOfTen);
        double[] multipliers = [1, 2.5, 5, 10];
        var closestMultiplier = multipliers.Aggregate((prev, curr) =>
            Math.Abs((curr * basePowerOfTen) - n) < Math.Abs((prev * basePowerOfTen) - n) ? curr : prev);
        return closestMultiplier * basePowerOfTen;
    }

    /// <summary>ported from: src/harness/harnessIO.ts:783-785 — <c>valueToString</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：<c>toLocaleString("en-US")</c> 是 en-US 的千分位与小数点，<c>roundToHumanLogarithm</c> 的结果又
    /// 总是整数（乘数 2.5 只与 10 的正整数次幂相乘；<c>n &lt; 10</c> 时是 0），所以 <c>N0</c> 与不变文化逐字符一致。
    /// </remarks>
    private static string ValueToString(double value)
        => RoundToHumanLogarithm(value).ToString("N0", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------------------------------------
    // scanner.ts 的行/列与行首映射
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/scanner.ts:548-550 — <c>getLineAndCharacterOfPosition</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：<c>ScannerUtilities.GetLineAndCharacterOfPosition</c> 取 <c>ISourceFileLike</c>，而移植中没有任何
    /// 类型实现该接口，所以这里保留 <c>SourceFile</c> 收窄 —— 与 <c>Program/Program.Host.cs</c>、
    /// <c>Utilities/Utilities.Binder.Diagnostics.cs</c> 中同样的两个助手、同样的理由。
    /// </remarks>
    private static LineAndCharacter GetLineAndCharacterOfPosition(SourceFile sourceFile, int position)
        => ScannerUtilities.ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);

    /// <summary>ported from: src/compiler/scanner.ts:160-172 — <c>getLineStarts</c>。</summary>
    private static IList<int> GetLineStarts(SourceFile sourceFile)
        => sourceFile.LineMap ?? (sourceFile.LineMap = ScannerUtilities.ComputeLineStarts(sourceFile.Text ?? ""));

    // ---------------------------------------------------------------------------------------------
    // getMeaningFromDeclaration（services/utilities.ts，移植中尚无）
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/services/utilities.ts:394-400 — <c>SemanticMeaning</c>。</summary>
    /// <remarks>PORT-NOTE：TS 声明为 <c>@internal const enum</c>，移植写出取值并标 <c>[Flags]</c>（§4.5）。</remarks>
    [Flags]
    private enum SemanticMeaning : uint
    {
        None = 0x0,
        Value = 0x1,
        Type = 0x2,
        Namespace = 0x4,
        All = Value | Type | Namespace,
    }

    /// <summary>ported from: src/services/utilities.ts:403-465 — <c>getMeaningFromDeclaration</c>。</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE：这个 helper 属于 <c>src/services/utilities.ts</c>（服务层），移植中还没有，而
    /// <c>typeWriter.ts:244</c> 依赖它，所以按本文件的"局部移植"惯例带 <see cref="SemanticMeaning"/> 一起放在这里。
    /// </para>
    /// <para>
    /// PORT-NOTE：<c>isInJSFile</c>/<c>getJSDocEnumTag</c>/<c>isAmbientModule</c>/<c>getModuleInstanceState</c> 都是
    /// 已落地的 <c>Utilities</c>/<c>Binder</c> 成员，直接调用，不另抄一份。
    /// </para>
    /// </remarks>
    private static SemanticMeaning GetMeaningFromDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.VariableDeclaration:
                return Utilities.IsInJSFile(node) && Utilities.GetJSDocEnumTag(node) is not null
                    ? SemanticMeaning.All
                    : SemanticMeaning.Value;

            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.CatchClause:
            case SyntaxKind.JsxAttribute:
                return SemanticMeaning.Value;

            case SyntaxKind.TypeParameter:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.TypeLiteral:
                return SemanticMeaning.Type;

            case SyntaxKind.JSDocTypedefTag:
                // If it has no name node, it shares the name with the value declaration below it.
                return ((JSDocTypedefTag)node).Name is null
                    ? SemanticMeaning.Value | SemanticMeaning.Type
                    : SemanticMeaning.Type;

            case SyntaxKind.EnumMember:
            case SyntaxKind.ClassDeclaration:
                return SemanticMeaning.Value | SemanticMeaning.Type;

            case SyntaxKind.ModuleDeclaration:
                if (Utilities.IsAmbientModule(node))
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                else if (Binder.GetModuleInstanceState((ModuleDeclaration)node) == ModuleInstanceState.Instantiated)
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                else
                {
                    return SemanticMeaning.Namespace;
                }

            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.NamedImports:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
                return SemanticMeaning.All;

            // An external module can be a Value
            case SyntaxKind.SourceFile:
                return SemanticMeaning.Namespace | SemanticMeaning.Value;
        }

        return SemanticMeaning.All;
    }

    /// <summary>ported from: src/harness/typeWriter.ts:1-342 — <c>TypeWriterWalker</c> 的结果形状。</summary>
    /// <remarks>
    /// PORT-NOTE：TS 的 <c>TypeWriterTypeResult</c>/<c>TypeWriterSymbolResult</c>（<c>typeWriter.ts:6-18</c>）是同一个
    /// 对象的两个面，走查的两个入口只是填不同的槽位，所以移植用一个 record 表达（<c>tools/BaselineDiff/ts-baseline.cjs</c>
    /// 亦然）；<c>syntaxKind</c> 是发布出去的接口的一部分，正文不用它。
    /// </remarks>
    private sealed record TypeWriterResult(
        int Line,
        SyntaxKind Kind,
        string SourceText,
        string? Symbol,
        string? Type,
        string? Underline);

    /// <summary>ported from: src/harness/typeWriter.ts:179-342 — <c>TypeWriterWalker</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：参照实现把 <c>currentSourceFile</c> 存成 walker 上的可变字段，两个入口各自先赋值再走查；移植同样
    /// 如此，因为 <c>writeTypeOrSymbol</c> 的三处查询都以它为上下文。
    /// </remarks>
    private sealed class TypeWriterWalker(PortProgram program, bool hadErrorBaseline)
    {
        private readonly ITypeChecker _checker = program.GetTypeChecker();

        /// <summary>ported from: src/harness/typeWriter.ts:327-329 — <c>(declaration as any).__symbolTestOutputCache</c>。</summary>
        /// <remarks>
        /// PORT-NOTE：TS 把结果挂在声明节点上（JS 对象可以当 expando 用），移植的节点没有该槽位，所以用按引用比较
        /// 的字典缓存；缓存值只由声明自身的文件与位置决定，字典只记住相同结果，输出与参照实现一致。
        /// </remarks>
        private readonly Dictionary<Node, string> _symbolTestOutputCache = new(ReferenceEqualityComparer.Instance);

        private SourceFile _currentSourceFile = null!;

        /// <summary>ported from: src/harness/typeWriter.ts:190-195 — <c>getSymbols</c>。</summary>
        internal IEnumerable<TypeWriterResult> GetSymbols(string fileName) => VisitNode(fileName, isSymbolWalk: true);

        /// <summary>ported from: src/harness/typeWriter.ts:197-202 — <c>getTypes</c>。</summary>
        internal IEnumerable<TypeWriterResult> GetTypes(string fileName) => VisitNode(fileName, isSymbolWalk: false);

        /// <summary>ported from: src/harness/typeWriter.ts:204-214 — <c>visitNode</c>。</summary>
        private IEnumerable<TypeWriterResult> VisitNode(string fileName, bool isSymbolWalk)
        {
            _currentSourceFile = Debug.CheckDefined(program.GetSourceFile(fileName), $"the program has no source file for '{fileName}'");
            foreach (var node in ForEachASTNode(_currentSourceFile))
            {
                if (Utilities.IsExpressionNode(node) || node.Kind == SyntaxKind.Identifier || Utilities.IsDeclarationName(node))
                {
                    var result = WriteTypeOrSymbol(node, isSymbolWalk);
                    if (result is not null)
                    {
                        yield return result;
                    }
                }
            }
        }

        /// <summary>ported from: src/harness/typeWriter.ts:216-221 — <c>isImportStatementName</c>。</summary>
        private static bool IsImportStatementName(Node node)
        {
            if (NodeTests.IsImportSpecifier(node.Parent!)
                && (ReferenceEquals(((ImportSpecifier)node.Parent!).Name, node) || ReferenceEquals(((ImportSpecifier)node.Parent!).PropertyName, node))) return true;
            if (NodeTests.IsImportClause(node.Parent!) && ReferenceEquals(((ImportClause)node.Parent!).Name, node)) return true;
            if (NodeTests.IsImportEqualsDeclaration(node.Parent!) && ReferenceEquals(((ImportEqualsDeclaration)node.Parent!).Name, node)) return true;
            return false;
        }

        /// <summary>ported from: src/harness/typeWriter.ts:223-227 — <c>isExportStatementName</c>。</summary>
        private static bool IsExportStatementName(Node node)
        {
            if (NodeTests.IsExportAssignment(node.Parent!) && ReferenceEquals(((ExportAssignment)node.Parent!).Expression, node)) return true;
            if (NodeTests.IsExportSpecifier(node.Parent!)
                && (ReferenceEquals(((ExportSpecifier)node.Parent!).Name, node) || ReferenceEquals(((ExportSpecifier)node.Parent!).PropertyName, node))) return true;
            return false;
        }

        /// <summary>ported from: src/harness/typeWriter.ts:229-234 — <c>isIntrinsicJsxTag</c>。</summary>
        /// <remarks>
        /// PORT-NOTE：TS 的三个 <c>isJsx…Element</c> 守卫把 <c>p</c> 收窄到一个带 <c>tagName</c> 的联合，移植的三个
        /// 节点类各自声明 <c>TagName</c>，所以收窄写成 switch；<c>node.getText()</c> 在标识符上是源码切片，
        /// 即 <c>Utilities.GetTextOfNode</c>。
        /// </remarks>
        private static bool IsIntrinsicJsxTag(Node node)
        {
            Node? tagName = node.Parent switch
            {
                JsxOpeningElement openingElement => openingElement.TagName,
                JsxClosingElement closingElement => closingElement.TagName,
                JsxSelfClosingElement selfClosingElement => selfClosingElement.TagName,
                _ => null,
            };
            if (!ReferenceEquals(tagName, node)) return false;
            return Utilities.IsIntrinsicJsxName(Utilities.GetTextOfNode(node));
        }

        /// <summary>ported from: src/harness/typeWriter.ts:236-342 — <c>writeTypeOrSymbol</c>。</summary>
        private TypeWriterResult? WriteTypeOrSymbol(Node node, bool isSymbolWalk)
        {
            var actualPos = ScannerUtilities.SkipTrivia(_currentSourceFile.Text ?? "", node.Pos, stopAfterLineBreak: false, stopAtComments: false);
            var lineAndCharacter = GetLineAndCharacterOfPosition(_currentSourceFile, actualPos);
            var sourceText = Utilities.GetSourceTextOfNodeFromSourceFile(_currentSourceFile, node);

            if (!isSymbolWalk)
            {
                // Don't try to get the type of something that's already a type.
                // Exception for `T` in `type T = something` because that may evaluate to some interesting type.
                if (Utilities.IsPartOfTypeNode(node)
                    || NodeTests.IsIdentifier(node)
                        && (GetMeaningFromDeclaration(node.Parent!) & SemanticMeaning.Value) == SemanticMeaning.None
                        && !(NodeTests.IsTypeAliasDeclaration(node.Parent!) && ReferenceEquals(((TypeAliasDeclaration)node.Parent!).Name, node)))
                {
                    return null;
                }

                if (NodeTests.IsOmittedExpression(node))
                {
                    return null;
                }

                // Workaround to ensure we output 'C' instead of 'typeof C' for base class expressions
                var type = Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(node.Parent!) ? _checker.GetTypeAtLocation(node.Parent) : null;
                if (type is null || (type.Flags & TypeFlags.Any) != 0) type = _checker.GetTypeAtLocation(node);

                // Distinguish `errorType`s from `any`s; but only if the file has no errors.
                string typeString;
                string? underline = null;
                if (!hadErrorBaseline
                    && (type.Flags & TypeFlags.Any) != 0
                    && !NodeTests.IsBindingElement(node.Parent!)
                    && !Utilities.IsPropertyAccessOrQualifiedName(node.Parent!)
                    && !IsLabelName(node)
                    && !(NodeTests.IsModuleDeclaration(node.Parent!) && Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)node.Parent!))
                    && !NodeTests.IsMetaProperty(node.Parent!)
                    && !IsImportStatementName(node)
                    && !IsExportStatementName(node)
                    && !IsIntrinsicJsxTag(node))
                {
                    typeString = ((IIntrinsicType)type).IntrinsicName!;
                }
                else
                {
                    const TypeFormatFlags typeFormatFlags = TypeFormatFlags.NoTruncation | TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.GenerateNamesForShadowedTypeParams;
                    var typeNode = _checker.TypeToTypeNode(
                        type,
                        node.Parent,
                        (NodeBuilderFlags)((int)(typeFormatFlags & TypeFormatFlags.NodeBuilderFlagsMask) | (int)NodeBuilderFlags.IgnoreErrors),
                        InternalNodeBuilderFlags.AllowUnresolvedNames,
                        tracker: null);
                    if (NodeTests.IsIdentifier(node)
                        && NodeTests.IsTypeAliasDeclaration(node.Parent!)
                        && ReferenceEquals(((TypeAliasDeclaration)node.Parent!).Name, node)
                        && typeNode is not null
                        && NodeTests.IsIdentifier((Node)typeNode)
                        && Utilities.IdText((Node)typeNode) == Utilities.IdText(node))
                    {
                        // for a complex type alias `type T = ...`, showing "T : T" isn't very helpful for type tests. When the type produced is the same as
                        // the name of the type alias, recreate the type string without reusing the alias name
                        typeNode = _checker.TypeToTypeNode(
                            type,
                            node.Parent,
                            (NodeBuilderFlags)((int)((typeFormatFlags | TypeFormatFlags.InTypeAlias) & TypeFormatFlags.NodeBuilderFlagsMask) | (int)NodeBuilderFlags.IgnoreErrors),
                            InternalNodeBuilderFlags.AllowUnresolvedNames,
                            tracker: null);
                    }

                    var underliningPrinter = UnderliningPrinter();
                    underliningPrinter.Reset();
                    underliningPrinter.Printer.WriteNode(EmitHint.Unspecified, typeNode as Node, _currentSourceFile, underliningPrinter.Writer);
                    typeString = underliningPrinter.Writer.GetText();
                    underline = underliningPrinter.Underliner.GetText();
                    underliningPrinter.Reset();
                }

                return new TypeWriterResult(lineAndCharacter.Line, node.Kind, sourceText, Symbol: null, Type: typeString, Underline: underline);
            }

            var symbol = _checker.GetSymbolAtLocation(node);
            if (symbol is null)
            {
                return null;
            }

            var symbolString = "Symbol(" + _checker.SymbolToString(symbol, node.Parent, meaning: null, flags: null);
            if (symbol.Declarations is not null)
            {
                var count = 0;
                foreach (var declaration in symbol.Declarations)
                {
                    if (count >= 5)
                    {
                        symbolString += $" ... and {symbol.Declarations.Count - count} more";
                        break;
                    }

                    count++;
                    symbolString += ", ";
                    var declarationNode = (Node)declaration;
                    if (_symbolTestOutputCache.TryGetValue(declarationNode, out var cached))
                    {
                        symbolString += cached;
                        continue;
                    }

                    var declSourceFile = Utilities.GetSourceFileOfNode(declarationNode)!;
                    var declLineAndCharacter = GetLineAndCharacterOfPosition(declSourceFile, declarationNode.Pos);
                    var fileName = Paths.GetBaseFileName(declSourceFile.FileName ?? "");
                    var isLibFile = LibFileNameRegExp().IsMatch(fileName);
                    var declText = $"Decl({fileName}, {(isLibFile ? "--" : declLineAndCharacter.Line.ToString(CultureInfo.InvariantCulture))}, {(isLibFile ? "--" : declLineAndCharacter.Character.ToString(CultureInfo.InvariantCulture))})";
                    symbolString += declText;
                    _symbolTestOutputCache[declarationNode] = declText;
                }
            }

            symbolString += ")";
            return new TypeWriterResult(lineAndCharacter.Line, node.Kind, sourceText, Symbol: symbolString, Type: null, Underline: null);
        }
    }

    /// <summary>ported from: src/harness/typeWriter.ts:32-45 — <c>forEachASTNode</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：显式栈的先序走查：弹出即产出，子节点按反序入栈（<c>forEachChild</c> 收集后逐个 <c>unshift</c>，
    /// 再 <c>push(...children)</c>），所以最左边的孩子最后入栈、最先弹出。
    /// </remarks>
    private static IEnumerable<Node> ForEachASTNode(Node node)
    {
        var work = new List<Node> { node };
        while (work.Count > 0)
        {
            var elem = work[^1];
            work.RemoveAt(work.Count - 1);
            yield return elem;

            var resChildren = new List<Node>();
            // push onto work queue in reverse order to maintain preorder traversal
            ForEachChildTable.ForEachChild(elem, child => resChildren.Insert(0, child));
            work.AddRange(resChildren);
        }
    }

    /// <summary>ported from: src/harness/typeWriter.ts:47-49 — <c>nodeIsFullySynthetic</c>。</summary>
    private static bool NodeIsFullySynthetic(Node node) => Utilities.NodeIsSynthesized(node) && Utilities.GetParseTreeNode(node) is null;

    /// <summary>ported from: src/services/utilities.ts:679-686 — <c>isLabelName</c>（<c>isLabelOfLabeledStatement</c>
    /// 与 <c>isJumpStatementTarget</c>）。</summary>
    /// <remarks>
    /// PORT-NOTE：这两个 helper 属于服务层、移植中还没有（<c>NodeTests</c> 只到 <c>IsBreakStatement</c>/
    /// <c>IsContinueStatement</c>），所以按局部移植惯例放在这里。
    /// </remarks>
    private static bool IsLabelName(Node node) => IsLabelOfLabeledStatement(node) || IsJumpStatementTarget(node);

    /// <summary>ported from: src/services/utilities.ts:679-681 — <c>isLabelOfLabeledStatement</c>。</summary>
    private static bool IsLabelOfLabeledStatement(Node node)
        => NodeTests.IsIdentifier(node)
            && node.Parent is LabeledStatement labeledStatement
            && ReferenceEquals(labeledStatement.Label, node);

    /// <summary>ported from: src/services/utilities.ts:673-676 — <c>isJumpStatementTarget</c>。</summary>
    private static bool IsJumpStatementTarget(Node node)
    {
        if (!NodeTests.IsIdentifier(node)) return false;
        return node.Parent switch
        {
            BreakStatement breakStatement => ReferenceEquals(breakStatement.Label, node),
            ContinueStatement continueStatement => ReferenceEquals(continueStatement.Label, node),
            _ => false,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 合成节点下划线打印机（typeWriter.ts:51-178）
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/harness/typeWriter.ts:51-178 — <c>createSyntheticNodeUnderliningPrinter</c> 的
    /// <c>memoize</c>。</summary>
    /// <remarks>
    /// PORT-NOTE：TS 把工厂的结果 memoize 成进程内单例，本移植同样只建一个（<c>Core.Memoize</c> 是移植的
    /// <c>memoize</c>），因为打印器与两个 writer 的状态由 <see cref="SyntheticNodeUnderliningPrinter.Reset"/>
    /// 显式复位，跨调用复用与参照实现一致。
    /// </remarks>
    private static readonly Func<SyntheticNodeUnderliningPrinter> s_underliningPrinter = Core.Memoize(() => new SyntheticNodeUnderliningPrinter());

    private static SyntheticNodeUnderliningPrinter UnderliningPrinter() => s_underliningPrinter();

    /// <summary>ported from: src/harness/typeWriter.ts:51-178 — <c>createSyntheticNodeUnderliningPrinter</c>。</summary>
    /// <remarks>
    /// <para>
    /// 打印机把类型节点渲染成两路文本：正文，和同宽的下划线（合成节点上写 <c>^</c>、其余写空格），于是
    /// <c>&gt;</c> 行的第二路就是类型里"哪一段是合成的"。
    /// </para>
    /// <para>
    /// PORT-NOTE：TS 在 <c>createPrinter</c> 的 handlers 字面量里只给 <c>onEmitNode</c>，并让 <c>writer</c> 对象把
    /// 每个成员转发给两路 writer；移植的 <see cref="IPrintHandlers"/>/<see cref="IEmitTextWriter"/> 是必填的接口，
    /// 所以 <see cref="UnderliningPrintHandlers"/> 补齐其余槽位、<see cref="UnderliningEmitTextWriter"/> 转发全部成员。
    /// </para>
    /// </remarks>
    private sealed class SyntheticNodeUnderliningPrinter
    {
        private readonly IEmitTextWriter _baseWriter = Utilities.CreateTextWriter("");
        private readonly IEmitTextWriter _underliner = Utilities.CreateTextWriter("");
        private bool _underlining;

        internal SyntheticNodeUnderliningPrinter()
        {
            Underliner = _underliner;
            Writer = new UnderliningEmitTextWriter(this, _baseWriter, _underliner);
            // createPrinter({ removeComments: true }, …)
            Printer = new Printer(new PrinterOptions { RemoveComments = true }, new UnderliningPrintHandlers(this));
        }

        internal Printer Printer { get; }

        internal IEmitTextWriter Writer { get; }

        internal IEmitTextWriter Underliner { get; }

        /// <summary>ported from: src/harness/typeWriter.ts:167-172 — <c>reset</c>。</summary>
        internal void Reset()
        {
            _underlining = false;
            _baseWriter.Clear();
            _underliner.Clear();
        }

        /// <summary>ported from: src/harness/typeWriter.ts:54-62 — <c>onEmitNode</c> 在合成边界上取反
        /// <c>underlining</c>。</summary>
        /// <remarks>
        /// PORT-NOTE：参照实现的 <c>node</c> 非空（<c>nodeIsFullySynthetic(node)</c> 会直接读它的 <c>pos</c>），移植的
        /// <see cref="IPrintHandlers.OnEmitNode"/> 槽位可空，所以这里的 <c>node is null</c> 归到"需要取反"那一支：
        /// 打印器唯一的调用点（<c>Printer.PipelineEmitWithNotification</c>）取非空 <c>Node</c>，两个答案都到不了。
        /// </remarks>
        internal void OnEmitNode(EmitHint hint, Node? node, Action<EmitHint, Node>? emitCallback)
        {
            if (node is null || NodeIsFullySynthetic(node) != _underlining)
            {
                // either node is synthetic and underlining needs to be enabled, or node is not synthetic and
                // underlining needs to be disabled
                _underlining = !_underlining;
                emitCallback?.Invoke(hint, node!);
                _underlining = !_underlining;
                return;
            }

            // underlining does not need to change
            emitCallback?.Invoke(hint, node!);
        }

        /// <summary>ported from: src/harness/typeWriter.ts:174-176 — <c>underlineFor</c>。</summary>
        internal string UnderlineFor(string s) => s.Length == 0 ? s : new string(_underlining ? '^' : ' ', s.Length);
    }

    /// <summary>ported from: src/harness/typeWriter.ts:53-64 — <c>createPrinter</c> 收到的 handlers 字面量。</summary>
    /// <remarks>
    /// PORT-NOTE：TS 只给 <c>onEmitNode</c>，其余成员在被读到时是 <c>undefined</c>，打印器为它们准备了缺省答案；移植
    /// 的接口要求每个成员都存在，所以这里逐槽照抄 <see cref="PrintHandlers.Default"/> 的答案（<c>hasGlobalName</c>
    /// 为 false、<c>isEmitNotificationEnabled</c> 为 true、<c>substituteNode</c> 是 no-emit 替换），十个可选回调留
    /// <c>null</c>（参照实现里就是 <c>undefined</c>）。
    /// </remarks>
    private sealed class UnderliningPrintHandlers(SyntheticNodeUnderliningPrinter owner) : IPrintHandlers
    {
        public bool HasGlobalName(string? name) => PrintHandlers.Default.HasGlobalName(name);

        public void OnEmitNode(EmitHint hint, Node? node, Action<EmitHint, Node>? emitCallback) => owner.OnEmitNode(hint, node, emitCallback);

        public bool IsEmitNotificationEnabled(Node? node) => PrintHandlers.Default.IsEmitNotificationEnabled(node);

        public Node SubstituteNode(EmitHint hint, Node? node) => PrintHandlers.Default.SubstituteNode(hint, node);

        public Action<EmitHint, Node, Action<EmitHint, Node>>? OnEmitSourceMapOfNode { get; set; }

        public Func<Node, SyntaxKind, Action<string>, int, Func<SyntaxKind, Action<string>, int, int>, int>? OnEmitSourceMapOfToken { get; set; }

        public Action<int>? OnEmitSourceMapOfPosition { get; set; }

        public Action<SourceFile>? OnSetSourceFile { get; set; }

        public Action<Node>? OnBeforeEmitNode { get; set; }

        public Action<Node>? OnAfterEmitNode { get; set; }

        public Action<NodeArray<object?>>? OnBeforeEmitNodeArray { get; set; }

        public Action<NodeArray<object?>>? OnAfterEmitNodeArray { get; set; }

        public Action<Node>? OnBeforeEmitToken { get; set; }

        public Action<Node>? OnAfterEmitToken { get; set; }
    }

    /// <summary>ported from: src/harness/typeWriter.ts:69-163 — 分给 <c>printer.writeNode</c> 的 writer 字面量。</summary>
    /// <remarks>
    /// <para>
    /// 每个成员把文本同时写向两路 writer，下划线那路收到的是 <see cref="SyntheticNodeUnderliningPrinter.UnderlineFor"/>；
    /// <c>getText</c>/<c>getTextPos</c> 等只读成员转发正文那路。
    /// </para>
    /// <para>
    /// PORT-NOTE：参照实现的字面量没有 <c>nonEscapingWrite</c>（只有片段转义 writer 才提供），打印器的
    /// <c>nonEscapingWrite</c> 在成员缺席时回退到 <c>write</c>（<c>emitter.ts:4909-4916</c>，移植的
    /// <c>Printer.NonEscapingWrite</c> 记录了同一件事）；移植的接口要求该成员存在，所以这里实现成回退本身，即
    /// 与 <c>Write</c> 相同。
    /// </para>
    /// </remarks>
    private sealed class UnderliningEmitTextWriter(
        SyntheticNodeUnderliningPrinter owner,
        IEmitTextWriter baseWriter,
        IEmitTextWriter underliner) : IEmitTextWriter
    {
        public void Write(string? s)
        {
            baseWriter.Write(s);
            underliner.Write(owner.UnderlineFor(s ?? ""));
        }

        public void WriteTrailingSemicolon(string? text)
        {
            baseWriter.WriteTrailingSemicolon(text);
            underliner.WriteTrailingSemicolon(owner.UnderlineFor(text ?? ""));
        }

        public void WriteComment(string? text)
        {
            baseWriter.WriteComment(text);
            underliner.WriteComment(owner.UnderlineFor(text ?? ""));
        }

        public string GetText() => baseWriter.GetText();

        public void RawWrite(string? s)
        {
            baseWriter.RawWrite(s);
            underliner.RawWrite(owner.UnderlineFor(s ?? ""));
        }

        public void WriteLiteral(string? s)
        {
            baseWriter.WriteLiteral(s);
            underliner.WriteLiteral(owner.UnderlineFor(s ?? ""));
        }

        public int GetTextPos() => baseWriter.GetTextPos();

        public int GetLine() => baseWriter.GetLine();

        public int GetColumn() => baseWriter.GetColumn();

        public int GetIndent() => baseWriter.GetIndent();

        public bool IsAtStartOfLine() => baseWriter.IsAtStartOfLine();

        public bool HasTrailingComment() => baseWriter.HasTrailingComment();

        public bool HasTrailingWhitespace() => baseWriter.HasTrailingWhitespace();

        public void NonEscapingWrite(string? text) => Write(text);

        public void WriteKeyword(string? text)
        {
            baseWriter.WriteKeyword(text);
            underliner.WriteKeyword(owner.UnderlineFor(text ?? ""));
        }

        public void WriteOperator(string? text)
        {
            baseWriter.WriteOperator(text);
            underliner.WriteOperator(owner.UnderlineFor(text ?? ""));
        }

        public void WritePunctuation(string? text)
        {
            baseWriter.WritePunctuation(text);
            underliner.WritePunctuation(owner.UnderlineFor(text ?? ""));
        }

        public void WriteSpace(string? text)
        {
            baseWriter.WriteSpace(text);
            underliner.WriteSpace(owner.UnderlineFor(text ?? ""));
        }

        public void WriteStringLiteral(string? text)
        {
            baseWriter.WriteStringLiteral(text);
            underliner.WriteStringLiteral(owner.UnderlineFor(text ?? ""));
        }

        public void WriteParameter(string? text)
        {
            baseWriter.WriteParameter(text);
            underliner.WriteParameter(owner.UnderlineFor(text ?? ""));
        }

        public void WriteProperty(string? text)
        {
            baseWriter.WriteProperty(text);
            underliner.WriteProperty(owner.UnderlineFor(text ?? ""));
        }

        public void WriteSymbol(string? text, ISymbol? symbol)
        {
            baseWriter.WriteSymbol(text, symbol);
            underliner.WriteSymbol(owner.UnderlineFor(text ?? ""), symbol);
        }

        public void WriteLine(bool? force)
        {
            baseWriter.WriteLine(force);
            underliner.WriteLine(force);
        }

        public void IncreaseIndent()
        {
            baseWriter.IncreaseIndent();
            underliner.IncreaseIndent();
        }

        public void DecreaseIndent()
        {
            baseWriter.DecreaseIndent();
            underliner.DecreaseIndent();
        }

        public void Clear()
        {
            baseWriter.Clear();
            underliner.Clear();
        }
    }

    /// <summary>ported from: src/harness/harnessIO.ts:802-814 — <c>getPerformanceBaselineValues</c> 返回的七个计数。</summary>
    private sealed record PerformanceBaselineValues(
        int StrictSubtype,
        int Subtype,
        int Identity,
        int Assignable,
        int TypeCount,
        int Instantiation,
        int Symbol);

    // 正则：TS 用字面量 /…/ 的地方（typeWriter.ts:232/315/325、harnessIO.ts:825/836/843、util.ts:7、typeWriter.ts:66
    // 的 createTextWriter("") 不算）。移植用 GeneratedRegex 之外的静态实例即可 —— 这些模式都是每进程固定的一小批。

    /// <summary>ported from: src/harness/typeWriter.ts:315 — <c>/lib.*\.d\.ts/i</c>。</summary>
    [GeneratedRegex("lib.*\\.d\\.ts", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LibFileNameRegExp();

    /// <summary>ported from: src/harness/harnessIO.ts:843 — <c>/\r?\n/g</c>（<c>sourceText.replace(/\r?\n/g, "")</c>）。</summary>
    [GeneratedRegex("\\r?\\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakRegExp();

    /// <summary>ported from: src/harness/harnessIO.ts:836/853 — <c>/^\s*[{|}]\s*$/</c>。</summary>
    [GeneratedRegex("^\\s*[{|}]\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BraceOnlyLineRegExp();

    /// <summary>ported from: src/harness/util.ts:7 — <c>/(?:(file:\/{3})|\/)\.(?:ts|lib|src)\//g</c>。</summary>
    [GeneratedRegex("(?:(file:/{3})|/)\\.(?:ts|lib|src)/", RegexOptions.CultureInvariant)]
    private static partial Regex TestPathPrefixRegExp();
}
