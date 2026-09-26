namespace TypeScript.Compiler.Tests;

/// <summary>
/// TS 的字符串值枚举（成员值是字符串字面量）被生成为 <c>const string</c> 常量类，而不是 C# enum。
/// 这里固定住这个形态与若干取值。
/// </summary>
public class StringValuedEnumTests
{
    [Fact]
    public void Extension_ValuesMatchTs()
    {
        Assert.Equal(".ts", Extension.Ts);
        Assert.Equal(".tsx", Extension.Tsx);
        Assert.Equal(".d.ts", Extension.Dts);
        Assert.Equal(".d.mts", Extension.Dmts);
        Assert.Equal(".d.cts", Extension.Dcts);
        Assert.Equal(".tsbuildinfo", Extension.TsBuildInfo);
        Assert.Equal(".json", Extension.Json);
    }

    [Fact]
    public void InternalSymbolName_ValuesMatchTs()
    {
        Assert.Equal("__call", InternalSymbolName.Call);
        Assert.Equal("__constructor", InternalSymbolName.Constructor);
        Assert.Equal("__new", InternalSymbolName.New);
        Assert.Equal("__resolving__", InternalSymbolName.Resolving);
        Assert.Equal("__importAttributes", InternalSymbolName.ImportAttributes);

        // 并非所有成员都是双下划线形式 —— 这三个既是内部符号也是合法的用户可见名字。
        Assert.Equal("export=", InternalSymbolName.ExportEquals);
        Assert.Equal("default", InternalSymbolName.Default);
        Assert.Equal("this", InternalSymbolName.This);
    }

    [Fact]
    public void PrivateIdentifierKind_ValuesMatchTs()
    {
        Assert.Equal("f", PrivateIdentifierKind.Field);
        Assert.Equal("m", PrivateIdentifierKind.Method);
        Assert.Equal("a", PrivateIdentifierKind.Accessor);
    }

    [Fact]
    public void ForegroundColorEscapeSequences_AreAnsiEscapes()
    {
        // TS 写作 "\u001b[90m"。生成器没有照抄字面量，而是解码成实际字符再按 C# 规则重新编码
        // （因为 C# 的 \x 是变长十六进制而 JS 的是定长两位）。
        Assert.Equal("\u001b[90m", ForegroundColorEscapeSequences.Grey);
        Assert.Equal("\u001b[91m", ForegroundColorEscapeSequences.Red);
        Assert.Equal("\u001b[93m", ForegroundColorEscapeSequences.Yellow);
        Assert.Equal("\u001b[94m", ForegroundColorEscapeSequences.Blue);
        Assert.Equal("\u001b[96m", ForegroundColorEscapeSequences.Cyan);
    }

    [Fact]
    public void ForegroundColorEscapeSequences_AreReEncodedNotCopied()
    {
        // 值必须以 ESC(0x1B) 开头，而不是字面的反斜杠-u-0-0-1-b 文本。
        // 若生成器照抄了 TS 字面量而没有解码，这条会失败。
        Assert.Equal('\u001b', ForegroundColorEscapeSequences.Grey[0]);
    }

    [Fact]
    public void StringValuedEnums_AreStaticConstantsClasses_NotCSharpEnums()
    {
        // 这是关键的设计约束：它们必须能在需要 string 的位置直接使用，
        // 所以不能是 C# enum（那会在每个调用点强制转换）。
        Type[] types =
        [
            typeof(Extension),
            typeof(InternalSymbolName),
            typeof(PrivateIdentifierKind),
            typeof(ForegroundColorEscapeSequences),
        ];

        foreach (var type in types)
        {
            Assert.False(type.IsEnum, $"{type.Name} should not be a C# enum");
            Assert.True(type.IsAbstract && type.IsSealed, $"{type.Name} should be a static class");

            var constants = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotEmpty(constants);
            Assert.All(constants, f => Assert.Equal(typeof(string), f.FieldType));
        }
    }
}
