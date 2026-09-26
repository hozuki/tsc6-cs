#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts — the two predicates the ES2016–ES2019 family reads, and the ordered set the
// two `super`-capture transforms build.
/// <summary>
/// The support members <c>es2017.ts</c> and <c>es2018.ts</c> read that the port's <c>Utilities.*</c> files do not yet
/// carry.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home): <c>isEffectiveStrictModeSourceFile</c> (<c>utilities.ts:2166-2192</c>) and
/// <c>isNodeWithPossibleHoistedDeclaration</c> (<c>utilities.ts:4964-4988</c>) are members of the reference's
/// <c>utilities.ts</c>, whose port home is <c>Utilities/Utilities.cs</c> — a landed file this slice may not edit. Both
/// are the reader-declares-it case <c>Utilities.Visitor.cs</c> records for <c>nullTransformationContext</c>: this file
/// is their only reader until the utilities slice states them, and their bodies are the reference's own tests.
/// </para>
/// <para>
/// PORT-NOTE (the ordered set): the reference's <c>capturedSuperProperties</c> is a
/// <c>Set&lt;__String&gt;</c> whose <em>insertion order</em> is observable —
/// <c>createSuperAccessVariableStatement</c> emits one accessor per name in the order
/// <c>es2017.ts</c>/<c>es2018.ts</c> visited them — and <c>HashSet&lt;T&gt;</c> does not preserve it. The
/// <see cref="ES2017TransformSupport.OrderedSet{T}"/> member below states a <c>List&lt;T&gt;</c> plus a
/// <c>HashSet&lt;T&gt;</c> with exactly the three operations the reference performs on that set (<c>add</c>,
/// <c>forEach</c> in iteration order and <c>new Set(other)</c> for the shadowing copy).
/// </para>
/// </remarks>
internal static class ES2017TransformSupport
{
    public static IEnumerable<Node>? BindingPatternElements(Node node)
        => node.Kind switch
        {
            SyntaxKind.ObjectBindingPattern => ((ObjectBindingPattern)node).Elements,
            SyntaxKind.ArrayBindingPattern => ((ArrayBindingPattern)node).Elements,
            _ => null,
        };

    /// <summary>A <c>Set&lt;T&gt;</c> that keeps the reference's insertion order.</summary>
    /// <remarks>PORT-NOTE: see the file header; the three members are the ones the reference's
    /// <c>capturedSuperProperties</c> set uses.</remarks>
    internal sealed class OrderedSet<T> : IEnumerable<T>
        where T : notnull
    {
        private readonly List<T> _items = [];
        private readonly HashSet<T> _seen = [];

        public OrderedSet()
        {
        }

        /// <summary><c>new Set(other)</c>.</summary>
        public OrderedSet(IEnumerable<T> other)
        {
            foreach (var item in other)
            {
                Add(item);
            }
        }

        public int Count => _items.Count;

        /// <summary><c>set.add(value)</c>.</summary>
        public bool Add(T item)
        {
            if (!_seen.Add(item))
            {
                return false;
            }
            _items.Add(item);
            return true;
        }

        /// <summary><c>set.has(value)</c>.</summary>
        public bool Contains(T item) => _seen.Contains(item);

        /// <summary><c>set.delete(value)</c>.</summary>
        public bool Remove(T item) => _seen.Remove(item) && _items.Remove(item);

        /// <summary><c>set.forEach((_, key) =&gt; …)</c> — the values in insertion order.</summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
