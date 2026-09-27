namespace TypeScript.Compiler;

// ported from: tsc/internal/core/linkstore.go:38-88 — PagedLinkStore
/// <summary>
/// An id-keyed table of link objects, stored as lazily allocated 256-element pages.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS states the checker's two link tables as sparse JS arrays written past their end, where a hole reads as
/// <c>undefined</c> and the write grows the array (`symbolLinks[id] ??= new SymbolLinks()`, checker.ts:2932-2941). The
/// port used to spell that as a <c>List&lt;T?&gt;</c> padded with <c>null</c> up to the id. Both cost a reference per id
/// ever handed out — and the ids are <em>process-global</em> counters starting at 1, so the table is sized by every node
/// the program has parsed, not by what the checker looked at — and a <c>List</c> doubling copies the whole table
/// (tens of MB per copy on a large program).
/// </para>
/// <para>
/// The Go port hit the same problem and solved it with <c>PagedLinkStore</c>
/// (<c>tsc/internal/core/linkstore.go:38-88</c>: <c>pageList []*[256]V</c> addressed by <c>id &gt;&gt; 8</c>, with
/// <c>pageMap</c> for the non-dense variant). The port states that shape: a page is allocated the first time an id
/// inside it is written, so a range nobody touches costs nothing, and a write past the end allocates one page instead of
/// copying the table. A page of 256 references is 2 KiB, which is what a lookup walks.
/// </para>
/// <para>
/// PORT-NOTE (concurrency): this is also what makes a per-shard link table cheap enough to be worth having — one store
/// per checker is what lets several checkers run at once without sharing mutable state (docs/architecture.md §18.4).
/// A single store is not thread-safe, exactly like the two it replaced; the phases that fan out give each worker its own.
/// </para>
/// </remarks>
internal sealed class PagedLinkStore<T>
    where T : class, new()
{
    /// <summary>How many ids one page covers: Go's <c>pageSize = 256</c> (linkstore.go:45).</summary>
    private const int PageShift = 8;

    private const int PageSize = 1 << PageShift;

    private const int PageMask = PageSize - 1;

    /// <summary>The pages, allocated on demand: page <c>n</c> holds the ids <c>[n &lt;&lt; 8, (n + 1) &lt;&lt; 8)</c>.</summary>
    private readonly List<T?[]> _pages = [];

    /// <summary>
    /// The link object for <paramref name="id"/>, creating it (and its page) on first use — the reference's
    /// <c>links[id] ??= new Links()</c>.
    /// </summary>
    internal T GetOrAdd(int id)
    {
        Debug.Assert(id >= 0, "link ids start at 1");
        var page = Page(id, create: true)!;
        return page[id & PageMask] ??= new T();
    }

    /// <summary>
    /// The link object for <paramref name="id"/>, or <c>null</c> when it was never created — the reference's
    /// <c>links[id]</c> answer for a hole, without growing the table the way a read of a padded list has to.
    /// </summary>
    internal T? TryGet(int id)
    {
        if (id < 0)
        {
            return null;
        }

        return Page(id, create: false)?[id & PageMask];
    }

    /// <summary>Whether any link object exists for <paramref name="id"/> (the table's own view of "has been computed").</summary>
    internal bool Contains(int id) => TryGet(id) is not null;

    private T?[]? Page(int id, bool create)
    {
        var index = id >> PageShift;
        if (index < _pages.Count)
        {
            return _pages[index];
        }

        if (!create)
        {
            return null;
        }

        while (_pages.Count <= index)
        {
            _pages.Add(new T?[PageSize]);
        }

        return _pages[index];
    }
}
