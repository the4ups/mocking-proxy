using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace TypedMock.Core.Hosting;

public static class StubServerHost
{
    public static Task<WebApplication> StartAsync(Assembly assembly, int port = 0) =>
        StartInternalAsync(assembly, null, port);

    public static Task<WebApplication> StartAsync(Type controllerType, int port = 0)
    {
        if (!typeof(ControllerBase).IsAssignableFrom(controllerType))
        {
            throw new ArgumentException($"Type '{controllerType.Name}' must inherit from ControllerBase.", nameof(controllerType));
        }

        return StartInternalAsync(controllerType.Assembly, controllerType, port);
    }

    public static Task<WebApplication> StartAsync<TController>(int port = 0) where TController : class =>
        StartAsync(typeof(TController), port);

    private static async Task<WebApplication> StartInternalAsync(Assembly assembly, Type? specificControllerType, int port)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        builder.WebHost.UseKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, port);
        });

        builder.Services.AddRouting();
        var mvcBuilder = builder.Services.AddControllers()
            .AddApplicationPart(assembly)
            .AddControllersAsServices();

        if (specificControllerType != null)
        {
            mvcBuilder.ConfigureApplicationPartManager(manager =>
            {
                manager.FeatureProviders.Add(new SingleControllerFeatureProvider(specificControllerType));
            });
        }

        var app = builder.Build();
        app.UseMiddleware<StubExceptionMiddleware>();

        app.Use(async (context, next) =>
        {
            var scenarioName = context.Request.Headers["X-Mock-Scenario"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                scenarioName = context.Request.Query["__scenario"].FirstOrDefault();
            }
            scenarioName ??= "default";
            context.Items["TypedMock.Scenario"] = scenarioName;

            await next(context);
        });

        app.MapControllers();

        await app.StartAsync();
        return app;
    }

    public static string GetServerUrl(WebApplication app)
    {
        var serverAddresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return serverAddresses?.Addresses.FirstOrDefault() ?? throw new InvalidOperationException("Server has no bound addresses.");
    }

    private class SingleControllerFeatureProvider(Type controllerType) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            for (var i = feature.Controllers.Count - 1; i >= 0; i--)
            {
                if (feature.Controllers[i].AsType() != controllerType)
                {
                    feature.Controllers.RemoveAt(i);
                }
            }
        }
    }
}
