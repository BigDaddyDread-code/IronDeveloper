using BookSeller.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BookSeller.Domain.Tests;

[TestClass]
public sealed class PricingServiceTests
{
    [TestMethod]
    public void PriceFor_SingleCopy_IsTheBookPrice()
    {
        var pricing = new PricingService();
        var book = new Book("isbn-1", "Title", "Author", 20m);

        Assert.AreEqual(20m, pricing.PriceFor(book, 1));
    }

    [TestMethod]
    public void PriceFor_MultipleCopies_IsFlatMultiple()
    {
        var pricing = new PricingService();
        var book = new Book("isbn-1", "Title", "Author", 20m);

        Assert.AreEqual(100m, pricing.PriceFor(book, 5));
    }
}
