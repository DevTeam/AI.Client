// Prints the monthly sales report to the console.
var sales = SalesData.Load();
foreach (var line in ReportBuilder.Build(sales))
    Console.WriteLine(line);
