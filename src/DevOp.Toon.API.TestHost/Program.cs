using DevOp.Toon.API;
using DevOp.Toon.API.TestHost.Services;
using DevOp.Toon.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ProductDataOptions>(options =>
{
    options.DataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "prods.json");
});
builder.Services.AddSingleton<ProductDataService>();
builder.Services.AddControllers().AddToon(true);

var app = builder.Build();

app.MapControllers();

app.Run();
