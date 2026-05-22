using BookSeller;

var inventory = new InventoryService();
inventory.AddStock(3);

if (inventory.Quantity != 3)
{
    Console.Error.WriteLine($"Expected quantity 3, actual {inventory.Quantity}.");
    return 1;
}

try
{
    inventory.AddStock(-1);
    Console.Error.WriteLine("Expected negative stock to be rejected.");
    return 1;
}
catch (ArgumentOutOfRangeException)
{
    Console.WriteLine("BookSeller inventory fixture tests passed.");
    return 0;
}
