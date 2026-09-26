namespace TypeScript.Compiler.Tests;

/// <summary>
/// Scanner 的 token 流。
/// </summary>
/// <remarks>
/// 这批测试是 M3 的验收：前面几步（诊断表、数据表、纯函数、trivia）都只是零件，这里才第一次把整台机器
/// 转起来。期望值取自 TS 6.0.3 <c>lib/typescript.js</c> 的 <c>createScanner</c> 与同名导出。
/// <para>
/// 注意几个 token 只有配合 reScan 才是完整的：`&gt;&gt;&gt;=` 由 scanner 切成若干个 `&gt;` 再靠
/// <c>ReScanGreaterToken</c> 合并（parser 就是这么用的），模板中段靠 <c>ReScanTemplateToken</c> 收尾，
/// JSX 文本要靠 <c>ScanJsxToken</c>。
/// </para>
/// </remarks>
public class ScannerTests
{
    private static Scanner NewScanner(
        string text,
        ScriptTarget target = ScriptTarget.ESNext,
        bool skipTrivia = true,
        LanguageVariant variant = LanguageVariant.Standard,
        ErrorCallback? onError = null)
        => Scanner.Create(target, skipTrivia, variant, text, onError);

    private static List<(SyntaxKind Kind, string Text)> Tokenize(
        string text,
        ScriptTarget target = ScriptTarget.ESNext,
        bool skipTrivia = true,
        LanguageVariant variant = LanguageVariant.Standard)
    {
        var scanner = NewScanner(text, target, skipTrivia, variant);
        var result = new List<(SyntaxKind, string)>();
        while (true)
        {
            var kind = scanner.Scan();
            if (kind == SyntaxKind.EndOfFileToken) break;
            result.Add((kind, scanner.GetTokenText()));
        }

        return result;
    }

    [Fact]
    public void Scan_TokenisesADeclaration()
    {
        Assert.Equal(
            [
                (SyntaxKind.ConstKeyword, "const"),
                (SyntaxKind.Identifier, "x"),
                (SyntaxKind.EqualsToken, "="),
                (SyntaxKind.NumericLiteral, "1"),
                (SyntaxKind.PlusToken, "+"),
                (SyntaxKind.NumericLiteral, "2"),
                (SyntaxKind.SemicolonToken, ";"),
            ],
            Tokenize("const x = 1 + 2;"));
    }

    [Fact]
    public void Scan_PicksTheLongestOperatorSpellingItCanSee()
    {
        Assert.Equal(
            [
                (SyntaxKind.EqualsGreaterThanToken, "=>"),
                (SyntaxKind.QuestionQuestionEqualsToken, "??="),
                (SyntaxKind.DotDotDotToken, "..."),
                (SyntaxKind.QuestionDotToken, "?."),
                (SyntaxKind.AsteriskAsteriskEqualsToken, "**="),
            ],
            Tokenize("=> ??= ... ?. **="));

        // A run of `>` is split by scan and merged by reScanGreaterToken, which is what the parser does.
        var scanner = NewScanner(">>>=");
        Assert.Equal(SyntaxKind.GreaterThanToken, scanner.Scan());
        Assert.Equal(SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken, scanner.ReScanGreaterToken());
        Assert.Equal(">>>=", scanner.GetTokenText());
    }

    [Fact]
    public void Scan_ReportsTriviaWhenAskedTo()
    {
        var scanner = NewScanner("  // c\nx", skipTrivia: false);
        var kinds = new List<SyntaxKind>();
        while (true)
        {
            var kind = scanner.Scan();
            kinds.Add(kind);
            if (kind == SyntaxKind.EndOfFileToken) break;
        }

        Assert.Equal(
            [
                SyntaxKind.WhitespaceTrivia,
                SyntaxKind.SingleLineCommentTrivia,
                SyntaxKind.NewLineTrivia,
                SyntaxKind.Identifier,
                SyntaxKind.EndOfFileToken,
            ],
            kinds);
    }

    [Fact]
    public void Scan_WritesNumberLiteralsInTheFormTsDoes()
    {
        // TS normalises every numeric literal through JS's number → string conversion, so the token value is the
        // decimal spelling, not the source text.
        Assert.Equal("255", Value("0xFF"));
        Assert.Equal("31", Value("0x1F"));
        Assert.Equal("5", Value("0b101"));
        Assert.Equal("15", Value("0o17"));
        Assert.Equal("1.5", Value("1.5"));
        Assert.Equal("1000", Value("1e3"));

        // A bigint keeps its digits and the `n`.
        Assert.Equal("123n", Value("123n", SyntaxKind.BigIntLiteral));
        // A hex bigint keeps the hex digits (only the 0b/0o forms are re-based to decimal), lower-cased.
        Assert.Equal("0xffffffffffffffffn", Value("0xFFFFFFFFFFFFFFFFn", SyntaxKind.BigIntLiteral));
        Assert.Equal("5n", Value("0b101n", SyntaxKind.BigIntLiteral));

        static string? Value(string text, SyntaxKind expected = SyntaxKind.NumericLiteral)
        {
            var scanner = NewScanner(text);
            Assert.Equal(expected, scanner.Scan());
            return scanner.GetTokenValue();
        }
    }

    [Fact]
    public void Scan_ReadsStringsAndEscapes()
    {
        Assert.Equal("ab", Value("'ab'"));
        Assert.Equal("a\nb", Value(@"'a\nb'"));
        Assert.Equal("a\nb", Value(@"'a\x0ab'".Replace("a", "a", StringComparison.Ordinal)));
        Assert.Equal("\u0001", Value(@"'\x01'"));

        static string? Value(string text)
        {
            var scanner = NewScanner(text);
            Assert.Equal(SyntaxKind.StringLiteral, scanner.Scan());
            return scanner.GetTokenValue();
        }
    }

    [Fact]
    public void Scan_ReadsTemplatePortions()
    {
        var scanner = NewScanner("`a${b}c`");

        Assert.Equal(SyntaxKind.TemplateHead, scanner.Scan());
        Assert.Equal("a", scanner.GetTokenValue());

        Assert.Equal(SyntaxKind.Identifier, scanner.Scan());
        Assert.Equal(SyntaxKind.CloseBraceToken, scanner.Scan());

        // The tail is only reached through reScanTemplateToken.
        Assert.Equal(SyntaxKind.TemplateTail, scanner.ReScanTemplateToken(isTaggedTemplate: false));
        Assert.Equal("c", scanner.GetTokenValue());
    }

    [Fact]
    public void Scan_ReadsRegularExpressionLiterals()
    {
        var scanner = NewScanner("/ab+c/gi");
        Assert.Equal(SyntaxKind.SlashToken, scanner.Scan());
        Assert.Equal(SyntaxKind.RegularExpressionLiteral, scanner.ReScanSlashToken());
        Assert.Equal("/ab+c/gi", scanner.GetTokenText());
    }

    [Fact]
    public void Scan_ReportsUnterminatedStringsAndRegexes()
    {
        var unterminatedString = NewScanner("'abc");
        Assert.Equal(SyntaxKind.StringLiteral, unterminatedString.Scan());
        Assert.True(unterminatedString.IsUnterminated());

        var unterminatedRegex = NewScanner("/abc");
        unterminatedRegex.Scan();
        unterminatedRegex.ReScanSlashToken();
        Assert.True(unterminatedRegex.IsUnterminated());
    }

    [Fact]
    public void Scan_ReportsDiagnosticsThroughTheErrorCallback()
    {
        var messages = new List<DiagnosticMessage>();
        var scanner = NewScanner("`abc", onError: (message, _, _) => messages.Add(message));

        while (scanner.Scan() != SyntaxKind.EndOfFileToken)
        {
            // drain
        }

        Assert.Contains(messages, static m => m.Code == Diagnostics.Unterminated_template_literal.Code);
    }

    [Fact]
    public void Scan_ReadsJsxTextAndAttributes()
    {
        var scanner = NewScanner("<div>text</div>", variant: LanguageVariant.JSX);
        Assert.Equal(SyntaxKind.LessThanToken, scanner.Scan());
        Assert.Equal(SyntaxKind.Identifier, scanner.Scan());
        Assert.Equal(SyntaxKind.GreaterThanToken, scanner.Scan());

        // Inside JSX, text is scanned by the JSX scanner rather than by scan().
        Assert.Equal(SyntaxKind.JsxText, scanner.ScanJsxToken());
        Assert.Equal("text", scanner.GetTokenValue());

        var attribute = NewScanner("a-b", variant: LanguageVariant.JSX);
        Assert.Equal(SyntaxKind.Identifier, attribute.Scan());
        Assert.Equal(SyntaxKind.Identifier, attribute.ScanJsxIdentifier());
        Assert.Equal("a-b", attribute.GetTokenValue());
    }

    [Fact]
    public void Scan_RecognisesJSDocComments()
    {
        // With trivia skipped the comment is consumed, but its JSDoc-ness is remembered on the *next* token.
        var jsDoc = NewScanner("/** @param x */\nx");
        Assert.Equal(SyntaxKind.Identifier, jsDoc.Scan());
        Assert.True(jsDoc.HasPrecedingJSDocComment());

        // A plain block comment is not JSDoc.
        var plain = NewScanner("/* @param x */\nx");
        Assert.Equal(SyntaxKind.Identifier, plain.Scan());
        Assert.False(plain.HasPrecedingJSDocComment());

        // When trivia is kept, the comment itself is the token.
        var withTrivia = NewScanner("/** @param x */", skipTrivia: false);
        Assert.Equal(SyntaxKind.MultiLineCommentTrivia, withTrivia.Scan());
        Assert.Equal("/** @param x */", withTrivia.GetTokenText());
    }

    [Fact]
    public void Scan_HonoursTheScriptTargetForIdentifierCharacters()
    {
        // U+10000 is an identifier character from ES2015 on only.
        Assert.Equal(SyntaxKind.Identifier, Tokenize("\U00010000")[0].Kind);
        Assert.Equal(SyntaxKind.Unknown, Tokenize("\U00010000", ScriptTarget.ES5)[0].Kind);
    }

    [Fact]
    public void Scan_CollectsCommentDirectives()
    {
        var scanner = NewScanner("// @ts-ignore\nx");
        scanner.Scan();

        var directives = scanner.GetCommentDirectives();
        Assert.NotNull(directives);
        Assert.Single(directives);
        Assert.Equal(CommentDirectiveType.Ignore, directives[0].Type);
    }

    [Fact]
    public void LookAheadAndTryScan_RestoreTheStateTheyShould()
    {
        var scanner = NewScanner("abc def");

        // Look-ahead always rewinds…
        Assert.Equal(SyntaxKind.Identifier, scanner.LookAhead(() => scanner.Scan()));
        Assert.Equal(SyntaxKind.Unknown, scanner.GetToken());

        // …and a normal scan then reads the same token again.
        Assert.Equal(SyntaxKind.Identifier, scanner.Scan());
        Assert.Equal("abc", scanner.GetTokenText());
        Assert.Equal("abc", scanner.GetTokenValue());
    }
}
