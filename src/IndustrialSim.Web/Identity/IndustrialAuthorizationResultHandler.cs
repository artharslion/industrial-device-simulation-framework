using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace IndustrialSim.Web.Api.V1;

public sealed class IndustrialAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await IndustrialSimProblemDetails.Result(
                StatusCodes.Status401Unauthorized,
                "Authentication required",
                "Authentication is required for this operation.",
                "authenticationRequired").ExecuteAsync(context);
            return;
        }
        if (authorizeResult.Forbidden)
        {
            await IndustrialSimProblemDetails.Result(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "The current role is not permitted to perform this operation.",
                "forbidden").ExecuteAsync(context);
            return;
        }
        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
