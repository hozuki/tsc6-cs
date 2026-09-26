namespace TypeScript.Compiler.Tests;

/// <summary>
/// TS 的字符串值枚举（成员值是字符串字面量）被生成为 <c>readonly struct</c> 包装：既能取出原始字符串，
/// 又保留名义类型，避免退化成裸 <c>string</c>。
/// </summary>
public class StringValuedEnumTests
{
    [Fact]
    public void Extension_ValuesMatchTs()
    {
        Assert.Equal(".ts", Extension.Ts.Value);
        Assert.Equal(".tsx", Extension.Tsx.Value);
        Assert.Equal(".d.ts", Extension.Dts.Value);
        Assert.Equal(".d.mts", Extension.Dmts.Value);
        Assert.Equal(".d.cts", Extension.Dcts.Value);
        Assert.Equal(".tsbuildinfo", Extension.TsBuildInfo.Value);
        Assert.Equal(".json", Extension.Json.Value);
    }

    [Fact]
    public void InternalSymbolName_ValuesMatchTs()
    {
        Assert.Equal("__call", InternalSymbolName.Call.Value);
        Assert.Equal("__constructor", InternalSymbolName.Constructor.Value);
        Assert.Equal("__new", InternalSymbolName.New.Value);
        Assert.Equal("__resolving__", InternalSymbolName.Resolving.Value);
        Assert.Equal("__importAttributes", InternalSymbolName.ImportAttributes.Value);

        // 并非所有成员都是双下划线形式 —— 这三个既是内部符号也是合法的用户可见名字。
        Assert.Equal("export=", InternalSymbolName.ExportEquals.Value);
        Assert.Equal("default", InternalSymbolName.Default.Value);
        Assert.Equal("this", InternalSymbolName.This.Value);
    }

    [Fact]
    public void PrivateIdentifierKind_ValuesMatchTs()
    {
        Assert.Equal("f", PrivateIdentifierKind.Field.Value);
        Assert.Equal("m", PrivateIdentifierKind.Method.Value);
        Assert.Equal("a", PrivateIdentifierKind.Accessor.Value);
    }

    [Fact]
    public void ForegroundColorEscapeSequences_AreAnsiEscapes()
    {
        // TS 写作 "\u001b[90m"。生成器没有照抄字面量，而是解码成实际字符再按 C# 规则重新编码
        // （C# 的 \x 是变长十六进制而 JS 的是定长两位）。
        Assert.Equal("\u001b[90m", ForegroundColorEscapeSequences.Grey.Value);
        Assert.Equal("\u001b[91m", ForegroundColorEscapeSequences.Red.Value);
        Assert.Equal("\u001b[93m", ForegroundColorEscapeSequences.Yellow.Value);
        Assert.Equal("\u001b[94m", ForegroundColorEscapeSequences.Blue.Value);
        Assert.Equal("\u001b[96m", ForegroundColorEscapeSequences.Cyan.Value);
    }

    [Fact]
    public void ForegroundColorEscapeSequences_AreReEncodedNotCopied()
    {
        // 值必须以 ESC(0x1B) 开头，而不是字面的反斜杠-u-0-0-1-b 文本。
        // 若生成器照抄了 TS 字面量而没有解码，这条会失败。
        Assert.Equal('\u001b', ForegroundColorEscapeSequences.Grey.Value[0]);
    }

    [Fact]
    public void StringEnums_AreReadonlyStructsImplementingIEquatable()
    {
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
            Assert.True(type.IsValueType, $"{type.Name} should be a struct");
            Assert.True(typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type),
                $"{type.Name} should implement IEquatable<{type.Name}>");
        }
    }

    [Fact]
    public void StringEnum_EqualityIsByUnderlyingValue()
    {
        var ts = Extension.Ts;
        var tsx = Extension.Tsx;

        Assert.Equal(ts, Extension.Ts);
        Assert.NotEqual(ts, tsx);
        Assert.True(ts == Extension.Ts);
        Assert.False(ts == tsx);
        Assert.True(ts != tsx);

        // 哈希必须与底层字符串一致，否则 Equals 与 GetHashCode 的契约会被破坏。
        Assert.Equal(string.GetHashCode(".ts", StringComparison.Ordinal), ts.GetHashCode());
    }

    [Fact]
    public void StringEnum_WorksAsDictionaryKeyAndInHashSet()
    {
        // 值语义必须成立，否则枚举不能安全地用作字典键。
        var byExtension = new Dictionary<Extension, string>
        {
            [Extension.Ts] = "typescript",
            [Extension.Js] = "javascript",
        };

        Assert.Equal("typescript", byExtension[Extension.Ts]);
        Assert.True(byExtension.ContainsKey(Extension.Ts));
        Assert.False(byExtension.ContainsKey(Extension.Json));

        Assert.Contains(Extension.Ts, new HashSet<Extension> { Extension.Ts });
    }

    [Fact]
    public void StringEnum_ToStringReturnsTheUnderlyingValue()
    {
        Assert.Equal(".ts", Extension.Ts.ToString());
        Assert.Equal("__call", InternalSymbolName.Call.ToString());
    }

    [Fact]
    public void StringEnum_DefaultValueIsEmptyStringAndDoesNotThrow()
    {
        // default(T) 在 C# 里总是存在（TS 没有这个概念）。取值不应抛异常，也不应伪装成某个成员。
        var unset = default(Extension);

        Assert.Equal(string.Empty, unset.Value);
        Assert.Equal(string.Empty, unset.ToString());
        Assert.NotEqual(Extension.Ts, unset);
        Assert.False(unset == Extension.Ts);
    }

    [Fact]
    public void StringEnum_HasNoImplicitConversionToString()
    {
        // 设计决策：不生成到 string 的隐式转换。包装的意义就在于调用点必须显式写 .Value，
        // 这样类型信息不会在赋值处悄悄丢失。若将来决定放开，这条测试应当被显式改掉。
        foreach (var type in new[] { typeof(Extension), typeof(InternalSymbolName) })
        {
            Assert.Null(type.GetMethod("op_Implicit"));
            Assert.Null(type.GetMethod("op_Explicit"));
        }
    }
}
