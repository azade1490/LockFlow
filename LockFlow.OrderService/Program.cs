using Microsoft.EntityFrameworkCore;

using LockFlow.OrderService.Lock;
using LockFlow.OrderService.LockWithHeartBeat;
using LockFlow.OrderService.Persistence.Data;

using StackExchange.Redis;
using LockFlow.OrderService.BackgroundWorkers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .Build();

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(configuration.GetConnectionString("ConnectionStringRedis")));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(configuration.GetConnectionString("ConnectionStringStoreDb"));
});

builder.Services.AddSingleton<IDistributedLockService, DistributedLockService>();
builder.Services.AddSingleton<IDistributedLockServiceWithHeartBeat, DistributedLockServiceWithHeartBeat>();

// این تنظیمات باعث می‌شود تمام BackgroundServiceها به جای اینکه یکی‌یکی شروع یا متوقف شوند، همزمان (Concurrent) شروع و متوقف شوند.
builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = true;
    options.ServicesStopConcurrently = true;
});
builder.Services.AddHostedService<OrderQueueWorker>();
builder.Services.AddHostedService<OrderQueueWorkerWithHeartBeat>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
