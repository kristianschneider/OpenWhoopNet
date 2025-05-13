using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;
using OpenWhoop.App.Services;
using OpenWhoop.Core.Data;

namespace OpenWhoop.MauiApp
{
    public static class MauiProgram
    {
        public static Microsoft.Maui.Hosting.MauiApp CreateMauiApp()
        {
            var builder = Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            // Configure database
            string dbFileName = "openwhoopnet.db";
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, dbFileName);

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            builder.Services.AddSingleton<DbService>(provider =>
                new DbService(provider.GetRequiredService<AppDbContext>()));

            var app = builder.Build();

            // Expose services
            App.SetServices(app.Services);

            // Apply migrations
            using (var scope = app.Services.CreateScope())
            {
                var dbService = scope.ServiceProvider.GetRequiredService<DbService>();
                dbService.Migrate();
            }

            return app;
        }
    }
}
