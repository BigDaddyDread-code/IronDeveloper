namespace BookSeller.Domain;

/// <summary>
/// An in-memory book catalog. Lookup is by ISBN only for now — the
/// "search by author" demo ticket asks the governed loop to add that.
/// </summary>
public sealed class Catalog
{
    private readonly Dictionary<string, Book> _booksByIsbn = new(StringComparer.Ordinal);

    public void Add(Book book) => _booksByIsbn[book.Isbn] = book;

    public Book? GetByIsbn(string isbn) =>
        _booksByIsbn.TryGetValue(isbn, out var book) ? book : null;

    public int Count => _booksByIsbn.Count;
}
