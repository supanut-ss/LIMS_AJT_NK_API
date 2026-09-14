using LIMS_AJT_NK_CallbackWorker;
using LIMS_AJT_NK_CallbackWorker.Data;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "LIMS AJT NK OCR Worker";
});
builder.Services.AddDbContext<WorkerDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Singleton,
    ServiceLifetime.Singleton);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
    await DbSeeder.SeedAsync(dbContext);
}

host.Run();
