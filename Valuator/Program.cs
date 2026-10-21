using StackExchange.Redis;

namespace Valuator;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorPages();
        builder.Services.AddSingleton<IConnectionMultiplexer>(services =>
            ConnectionMultiplexer.Connect(
                services.GetRequiredService<IConfiguration>().GetConnectionString("Redis") ?? "localhost:6379"));

        await using var messagePublisher = await MessagePublisher.CreateAsync(builder.Configuration);
        builder.Services.AddSingleton(messagePublisher);

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapRazorPages();

        await app.RunAsync();
    }
}
