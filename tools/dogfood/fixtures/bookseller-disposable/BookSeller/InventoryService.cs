namespace BookSeller;

public sealed class InventoryService
{
    public int Quantity { get; private set; }

    public void AddStock(int quantity)
    {
        Quantity += quantity;
    }
}
