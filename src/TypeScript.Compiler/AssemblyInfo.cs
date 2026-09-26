using System.Runtime.CompilerServices;

// 单元测试需要访问编译器内部成员（移植自 TS 的 /** @internal */ 结构在 C# 中多为 internal）。
[assembly: InternalsVisibleTo("TypeScript.Compiler.Tests")]

namespace TypeScript.Compiler;

/// <summary>
/// 程序集标识类型，供测试与反射定位编译器程序集。
/// </summary>
public static class AssemblyMarker
{
}
