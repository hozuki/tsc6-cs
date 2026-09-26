namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:7841-7979
/// <summary>
/// Character code constants used by the scanner and by text utilities.
/// </summary>
/// <remarks>
/// In TS this is a <c>const enum</c> whose members have implicit type <c>number</c>, and they are
/// compared directly against the result of <c>charCodeAt</c>. Here they are <c>const int</c>
/// members of a static class for the same reason: they take part in arithmetic and comparisons
/// with <see cref="string"/> char codes, which are <see cref="int"/>.
/// <para>
/// PORT-NOTE: the TS member <c>$</c> is not a legal C# identifier and is renamed to
/// <see cref="Dollar"/>.
/// </para>
/// </remarks>
internal static class CharacterCodes
{
    public const int EOF = -1;
    public const int NullCharacter = 0;
    public const int MaxAsciiCharacter = 0x7F;

    public const int LineFeed = 0x0A;              // \n
    public const int CarriageReturn = 0x0D;        // \r
    public const int LineSeparator = 0x2028;
    public const int ParagraphSeparator = 0x2029;
    public const int NextLine = 0x0085;

    // Unicode 3.0 space characters
    public const int Space = 0x0020;   // " "
    public const int NonBreakingSpace = 0x00A0;   //
    public const int EnQuad = 0x2000;
    public const int EmQuad = 0x2001;
    public const int EnSpace = 0x2002;
    public const int EmSpace = 0x2003;
    public const int ThreePerEmSpace = 0x2004;
    public const int FourPerEmSpace = 0x2005;
    public const int SixPerEmSpace = 0x2006;
    public const int FigureSpace = 0x2007;
    public const int PunctuationSpace = 0x2008;
    public const int ThinSpace = 0x2009;
    public const int HairSpace = 0x200A;
    public const int ZeroWidthSpace = 0x200B;
    public const int NarrowNoBreakSpace = 0x202F;
    public const int IdeographicSpace = 0x3000;
    public const int MathematicalSpace = 0x205F;
    public const int Ogham = 0x1680;

    // Unicode replacement character produced when a byte sequence is invalid
    public const int ReplacementCharacter = 0xFFFD;

    public const int Underscore = 0x5F;
    public const int Dollar = 0x24;

    public const int _0 = 0x30;
    public const int _1 = 0x31;
    public const int _2 = 0x32;
    public const int _3 = 0x33;
    public const int _4 = 0x34;
    public const int _5 = 0x35;
    public const int _6 = 0x36;
    public const int _7 = 0x37;
    public const int _8 = 0x38;
    public const int _9 = 0x39;

    public const int a = 0x61;
    public const int b = 0x62;
    public const int c = 0x63;
    public const int d = 0x64;
    public const int e = 0x65;
    public const int f = 0x66;
    public const int g = 0x67;
    public const int h = 0x68;
    public const int i = 0x69;
    public const int j = 0x6A;
    public const int k = 0x6B;
    public const int l = 0x6C;
    public const int m = 0x6D;
    public const int n = 0x6E;
    public const int o = 0x6F;
    public const int p = 0x70;
    public const int q = 0x71;
    public const int r = 0x72;
    public const int s = 0x73;
    public const int t = 0x74;
    public const int u = 0x75;
    public const int v = 0x76;
    public const int w = 0x77;
    public const int x = 0x78;
    public const int y = 0x79;
    public const int z = 0x7A;

    public const int A = 0x41;
    public const int B = 0x42;
    public const int C = 0x43;
    public const int D = 0x44;
    public const int E = 0x45;
    public const int F = 0x46;
    public const int G = 0x47;
    public const int H = 0x48;
    public const int I = 0x49;
    public const int J = 0x4A;
    public const int K = 0x4B;
    public const int L = 0x4C;
    public const int M = 0x4D;
    public const int N = 0x4E;
    public const int O = 0x4F;
    public const int P = 0x50;
    public const int Q = 0x51;
    public const int R = 0x52;
    public const int S = 0x53;
    public const int T = 0x54;
    public const int U = 0x55;
    public const int V = 0x56;
    public const int W = 0x57;
    public const int X = 0x58;
    public const int Y = 0x59;
    public const int Z = 0x5A;

    public const int Ampersand = 0x26;             // &
    public const int Asterisk = 0x2A;              // *
    public const int At = 0x40;                    // @
    public const int Backslash = 0x5C;             // \
    public const int Backtick = 0x60;              // `
    public const int Bar = 0x7C;                   // |
    public const int Caret = 0x5E;                 // ^
    public const int CloseBrace = 0x7D;            // }
    public const int CloseBracket = 0x5D;          // ]
    public const int CloseParen = 0x29;            // )
    public const int Colon = 0x3A;                 // :
    public const int Comma = 0x2C;                 // ,
    public const int Dot = 0x2E;                   // .
    public const int DoubleQuote = 0x22;           // "
    // PORT-NOTE: TS 成员名为 `equals`；PascalCase 化后会与 `object.Equals` 冲突，故命名 EqualsSign。
    public const int EqualsSign = 0x3D;            // =
    public const int Exclamation = 0x21;           // !
    public const int GreaterThan = 0x3E;           // >
    public const int Hash = 0x23;                  // #
    public const int LessThan = 0x3C;              // <
    public const int Minus = 0x2D;                 // -
    public const int OpenBrace = 0x7B;             // {
    public const int OpenBracket = 0x5B;           // [
    public const int OpenParen = 0x28;             // (
    public const int Percent = 0x25;               // %
    public const int Plus = 0x2B;                  // +
    public const int Question = 0x3F;              // ?
    public const int Semicolon = 0x3B;             // ;
    public const int SingleQuote = 0x27;           // '
    public const int Slash = 0x2F;                 // /
    public const int Tilde = 0x7E;                 // ~

    public const int Backspace = 0x08;             // \b
    public const int FormFeed = 0x0C;              // \f
    public const int ByteOrderMark = 0xFEFF;
    public const int Tab = 0x09;                   // \t
    public const int VerticalTab = 0x0B;           // \v
}
