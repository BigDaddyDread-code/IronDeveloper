using BookSeller.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BookSeller.Domain.Tests;

[TestClass]
public sealed class CatalogTests
{
    [TestMethod]
    public void Add_ThenGetByIsbn_ReturnsTheBook()
    {
        var catalog = new Catalog();
        var book = new Book("978-0-13-468599-1", "The Pragmatic Programmer", "Hunt & Thomas", 49.99m);

        catalog.Add(book);

        Assert.AreSame(book, catalog.GetByIsbn("978-0-13-468599-1"));
        Assert.AreEqual(1, catalog.Count);
    }

    [TestMethod]
    public void GetByIsbn_UnknownIsbn_ReturnsNull()
    {
        var catalog = new Catalog();

        Assert.IsNull(catalog.GetByIsbn("no-such-isbn"));
    }

    [TestMethod]
    public void Add_SameIsbnTwice_KeepsLatest()
    {
        var catalog = new Catalog();
        catalog.Add(new Book("isbn-1", "First Edition", "Author", 10m));
        catalog.Add(new Book("isbn-1", "Second Edition", "Author", 12m));

        Assert.AreEqual("Second Edition", catalog.GetByIsbn("isbn-1")!.Title);
        Assert.AreEqual(1, catalog.Count);
    }
}
