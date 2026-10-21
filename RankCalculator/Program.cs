using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace RankCalculator;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        if (args.Contains("--check-dependencies"))
        {
            await RankWorker.CheckDependenciesAsync(builder.Configuration);
            return;
        }
        builder.Services.AddHostedService<RankWorker>();
        using var host = builder.Build();
        await host.RunAsync();
    }
}
