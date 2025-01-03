
using FileHashComparer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = new ServiceCollection();


builder.AddLogging(config =>
{
    var logger = Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .MinimumLevel.Debug()
        .CreateLogger();
    
    config.ClearProviders();
    config.AddSerilog(logger, true);
});
builder.AddSingleton<RecursiveFileComparer>();

var serviceProvider = builder.BuildServiceProvider();

var comparer = serviceProvider.GetRequiredService<RecursiveFileComparer>();

Console.WriteLine("Input path to directory");
var startingDirectory = Console.ReadLine();

using var cts = new CancellationTokenSource();
var token = cts.Token;

try
{
    var duplicateFiles = await comparer.SearchDuplicateFilesAsync(startingDirectory!, token);
    Console.WriteLine("Found duplicate files:");

    foreach (var file in duplicateFiles)
    {
        Console.WriteLine(file);
    }
}
catch (Exception e)
{
    Console.WriteLine(e);
    Console.ReadLine();
    throw;
}