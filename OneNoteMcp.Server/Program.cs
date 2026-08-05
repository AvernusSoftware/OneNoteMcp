using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneNoteMcp.Core.Configuration;
using OneNoteMcp.Core.Services;
using OneNoteMcp.Core.Threading;
using OneNoteMcp.Server.Tools;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton<IStaThreadRunner, StaThreadRunner>();
builder.Services.AddSingleton<IOneNoteService, OneNoteComService>();
builder.Services.AddSingleton<OneNoteTools>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

IHost host = builder.Build();

await host.RunAsync();
