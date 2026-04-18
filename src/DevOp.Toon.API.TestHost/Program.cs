using DevOp.Toon.API;
using DevOp.Toon.API.TestHost.Services;
using DevOp.Toon.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ProductDataOptions>(options =>
{
    options.DataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "prods.json");
});
builder.Services.AddSingleton<ProductDataService>();
builder.Services.AddControllers().AddToon(options =>
{
    options.Encode.IgnoreNullOrEmpty = true;
    options.Encode.Delimiter = ToonDelimiter.COMMA;
    options.Encode.Indent = 1;
    options.Encode.ExcludeEmptyArrays = true;
    options.Encode.KeyFolding = ToonKeyFolding.Off;
    options.Encode.ObjectArrayLayout = ToonObjectArrayLayout.Columnar;
},true);

var app = builder.Build();

app.MapControllers();

app.Run();
