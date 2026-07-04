namespace BookSeller.Domain;

/// <summary>
/// A book in the catalog. Deliberately minimal: it currently accepts any values
/// (empty ISBN, negative price) — the "reject invalid books" demo ticket asks
/// the governed loop to add validation here.
/// </summary>
public sealed class Book
{
    public Book(string isbn, string title, string author, decimal price)
    {
        Isbn = isbn;
        Title = title;
        Author = author;
        Price = price;
    }

    public string Isbn { get; }
    public string Title { get; }
    public string Author { get; }
    public decimal Price { get; }
}
