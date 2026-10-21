namespace Valuator;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorPages();
        builder.Services.AddSingleton<ShardedRedis>();

        await using var messagePublisher = await MessagePublisher.CreateAsync(builder.Configuration);
        builder.Services.AddSingleton(messagePublisher);

        var app = builder.Build();
        await app.Services.GetRequiredService<ShardedRedis>().ConnectAsync();

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
