using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace RankCalculator;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ShardedRedis>();
        builder.Services.AddHostedService<RankWorker>();
        using var host = builder.Build();
        var redis = host.Services.GetRequiredService<ShardedRedis>();
        await redis.ConnectAsync();
        if (args.Contains("--check-dependencies"))
        {
            await RankWorker.CheckDependenciesAsync(redis, builder.Configuration);
            return;
        }
        await host.RunAsync();
    }
}
