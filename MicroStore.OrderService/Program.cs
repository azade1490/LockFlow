using Microsoft.EntityFrameworkCore;

using MicroStore.OrderService.Lock;
using MicroStore.OrderService.LockWithHeartBeat;
using MicroStore.OrderService.Persistence.Data;

using StackExchange.Redis;

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

builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = true;
    options.ServicesStopConcurrently = true;
});
builder.Services.AddHostedService<OrderQueueWorker>();
builder.Services.AddHostedService<OrderQueueWorkerWithHeartBeat>();

builder.Services.AddScoped<IDistributedLockService, DistributedLockService>();
builder.Services.AddScoped<IDistributedLockServiceWithHeartBeat, DistributedLockServiceWithHeartBeat>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
