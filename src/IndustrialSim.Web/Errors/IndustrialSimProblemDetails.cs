using IndustrialSim.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;

namespace IndustrialSim.Web;

public static class IndustrialSimProblemDetails
{
    public static IResult Result(int status, string title, string detail, string errorCode) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode
        });

    public static IApplicationBuilder UseIndustrialSimProblemDetails(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, title, code) = exception switch
            {
                SimulationConflictException conflict => (StatusCodes.Status409Conflict, "Simulation conflict", conflict.ErrorCode),
                SimulationNotFoundException notFound => (StatusCodes.Status404NotFound, "Simulation not found", notFound.ErrorCode),
                DeviceLaunchException launch => (StatusCodes.Status400BadRequest, "Invalid device launch", launch.ErrorCode),
                SocketException => (StatusCodes.Status409Conflict, "Protocol start failed", "protocolStartFailed"),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Persistence conflict", "persistenceConflict"),
                DbUpdateException => (StatusCodes.Status503ServiceUnavailable, "Persistence unavailable", "persistenceUnavailable"),
                ArgumentException => (StatusCodes.Status400BadRequest, "Validation failed", "validationFailed"),
                _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "unexpectedError")
            };
            context.Response.StatusCode = status;
            await Result(status, title, exception?.Message ?? title, code).ExecuteAsync(context);
        }));
        return app;
    }
}
