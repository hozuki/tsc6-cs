Use Git for version control. After each meaningful step, and milestone, make Git commit with proper messages. (format: "[AI-(name of the LLM)] (one-line description)\n\n(a little more detailed)")
C# source, testing harness are required. Use .NET 11 for development and testing. SDK is preinstalled.

.NET and C# langauge skills reference: `D:\Source\GitHub\dotnet-skills`.
TypeScript compiler source (TSC 6.0) and test sources: `D:\Source\GitHub\TypeScript`.
TSGO (TSC 7.0) is in its main branch, or can be directly accessed at `D:\Source\GitHub\TypeScript-Go`. The language feature is the same as TSC 6.0. Its implementation is recommended when porting to a static type system. However, Go's generics is not well-designed; use of generics can be more aggressive when porting to C#.
TSGO CLI is available at global `tsc` command.

For C# ports, correctness and accuracy are first priorities. Compiler performance and memory usage are also important.
Moreover, since .NET supports multi-threading, you must consider multithreaded (and thread-safe!) data structrues to accelerate the compiler. Try to do better than the Go port.
