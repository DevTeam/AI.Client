using Inventory;

// Self-checks: exits with 1 when the inventory logic misbehaves.
var stockService = new StockService();
stockService.Receive("apple", 10);
stockService.Receive("pear", 2);
var rejected = new OrderProcessor(stockService).Process([("apple", 4), ("pear", 5)]);
var reorder = new RestockPlanner(stockService, minimum: 3).ItemsToReorder(["apple", "pear"]).ToArray();

var ok = stockService.QuantityOf("apple") == 6 && rejected.SequenceEqual(["pear"]) && reorder.SequenceEqual(["pear"]);
Console.WriteLine(ok ? "Inventory checks passed." : "Inventory checks FAILED.");
return ok ? 0 : 1;
