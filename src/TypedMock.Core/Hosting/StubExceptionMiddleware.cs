using Microsoft.AspNetCore.Http;
using TypedMock.Core.Exceptions;

namespace TypedMock.Core.Hosting;

public class StubExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (StubHttpException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = ex.ResponseData ?? new { error = ex.Message, statusCode = ex.StatusCode };
            await context.Response.WriteAsJsonAsync(payload);
        }
        catch (StubNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = ex.Message, statusCode = 404 });
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            context.Response.StatusCode = (int)ex.StatusCode.Value;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = ex.Message, statusCode = (int)ex.StatusCode.Value });
        }
        catch (KeyNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = ex.Message, statusCode = 404 });
        }
    }
}
