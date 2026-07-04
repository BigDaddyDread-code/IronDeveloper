namespace BookSeller.Domain;

/// <summary>
/// Prices an order line. Quantity pricing is flat for now — the "bulk discount"
/// demo ticket asks the governed loop to add tiered discounting.
/// </summary>
public sealed class PricingService
{
    public decimal PriceFor(Book book, int quantity) => book.Price * quantity;
}
