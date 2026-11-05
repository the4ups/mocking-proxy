using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace TypedMock.Core.Hosting;

public static class StubServerHost
{
    public static async Task<WebApplication> StartAsync(Type controllerType, int port = 0)
    {
        if (!typeof(ControllerBase).IsAssignableFrom(controllerType))
        {
            throw new ArgumentException($"Type '{controllerType.Name}' must inherit from ControllerBase.", nameof(controllerType));
        }

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        builder.WebHost.UseKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, port);
        });

        builder.Services.AddRouting();
        builder.Services.AddControllers()
            .AddApplicationPart(controllerType.Assembly)
            .AddControllersAsServices();

        var app = builder.Build();
        app.UseMiddleware<StubExceptionMiddleware>();
        app.MapControllers();

        await app.StartAsync();
        return app;
    }

    public static Task<WebApplication> StartAsync<TController>(int port = 0) where TController : class =>
        StartAsync(typeof(TController), port);

    public static string GetServerUrl(WebApplication app)
    {
        var serverAddresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return serverAddresses?.Addresses.FirstOrDefault() ?? throw new InvalidOperationException("Server has no bound addresses.");
    }
}
