using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DevOp.Toon;
using DevOp.Toon.API;
using DevOp.Toon.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

var mode = args[0];
using var provider = DevOp.Toon.API.ToonServiceCollectionExtensions.AddToon(new ServiceCollection()).BuildServiceProvider();
var formatter = new ToonOutputFormatter();
var filter = (IAsyncResourceFilter)Activator.CreateInstance(typeof(ToonOutputFormatter).Assembly.GetType("DevOp.Toon.API.ToonVersionValidationFilter")!, true)!;
var filters = Array.Empty<IFilterMetadata>();
var descriptors = new ActionDescriptor();
var value = new Payload { Id = 42, Name = "Ada" };
using var http = new HttpClient(new Handler()) { BaseAddress = new Uri("https://benchmark.invalid") };
var client = new ToonClient(http, new ToonService());
async Task Operation()
{
    if (mode == "client")
    {
        var result = await client.GetAsync<Payload>("/");
        if (result?.Id != 42 || result.Name != "Ada") throw new Exception("Wrong client result.");
        return;
    }
    var context = new DefaultHttpContext { RequestServices = provider };
    context.Response.Body = Stream.Null;
    context.Request.Headers["X-Toon-Option-ProtocolVersion"] = "4.4";
    context.Request.Headers["X-Toon-Option-Indent"] = "2";
    var action = new ActionContext(context, new RouteData(), descriptors);
    await filter.OnResourceExecutionAsync(new ResourceExecutingContext(action, filters, new List<IValueProviderFactory>()), async () =>
    {
        await formatter.WriteResponseBodyAsync(new OutputFormatterWriteContext(context, (_,_) => TextWriter.Null, typeof(Payload), value), Encoding.UTF8);
        return new ResourceExecutedContext(action, filters);
    });
    if (context.Response.Headers["X-Toon-Version"] != "4.4") throw new Exception("Wrong output version.");
}
for (var i = 0; i < 1000; i++) await Operation();
var samples = new List<object>();
for (var s = 0; s < 30; s++)
{
    var start = GC.GetTotalAllocatedBytes(false);
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < 1000; i++) await Operation();
    sw.Stop();
    samples.Add(new { Milliseconds = sw.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetTotalAllocatedBytes(false) - start });
}
Console.WriteLine(JsonSerializer.Serialize(new { Mode = mode, OperationsPerSample = 1000, Warmups = 1000, Samples = samples }));
public sealed class Payload { public int Id { get; set; } public string Name { get; set; } = ""; }
sealed class Handler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Id: 42\nName: Ada", Encoding.UTF8, "application/toon") };
        response.Headers.Add("X-Toon-Version", "4.4");
        return Task.FromResult(response);
    }
}
