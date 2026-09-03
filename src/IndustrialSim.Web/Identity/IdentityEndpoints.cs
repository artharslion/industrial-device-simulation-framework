using System.Security.Claims;
using System.Text.Json;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Security;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Web.Api.V1;

public sealed record BootstrapRequest(string UserName, string Password);
public sealed record LoginRequest(string UserName, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CreateUserRequest(string UserName, string Password, string Role);
public sealed record UpdateSettingRequest(JsonElement Value);

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIndustrialSimIdentity(this IEndpointRouteBuilder endpoints)
    {
        using (var scope = endpoints.ServiceProvider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IndustrialSimDbContext>().Database.Migrate();

        var auth = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");
        auth.MapPost("/bootstrap", BootstrapAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapGet("/session", SessionAsync).AllowAnonymous();
        auth.MapPost("/password", ChangePasswordAsync).RequireAuthorization(IndustrialPolicies.Viewer);

        var users = endpoints.MapGroup("/api/v1/users").WithTags("Users").RequireAuthorization(IndustrialPolicies.Admin);
        users.MapGet("/", ListUsersAsync);
        users.MapPost("/", CreateUserAsync);
        users.MapDelete("/{userName}", DeleteUserAsync);

        var settings = endpoints.MapGroup("/api/v1/settings").WithTags("Settings").RequireAuthorization(IndustrialPolicies.Admin);
        settings.MapGet("/", async (ISettingCatalogRepository repository, CancellationToken token) => Results.Ok(await repository.ListAsync(token)));
        settings.MapPut("/{key}", UpsertSettingAsync);
        return endpoints;
    }

    private static async Task<IResult> BootstrapAsync(
        BootstrapRequest request,
        UserManager<IndustrialSimUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        if (await userManager.Users.AnyAsync())
            return IndustrialSimProblemDetails.Result(409, "Bootstrap closed", "The bootstrap administrator has already been created.", "bootstrapClosed");
        foreach (var role in IndustrialRoles.All)
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                if (!roleResult.Succeeded) return IdentityFailure(roleResult, "roleCreationFailed");
            }
        var user = new IndustrialSimUser { UserName = request.UserName };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded) return IdentityFailure(created, "invalidBootstrapCredential");
        var assigned = await userManager.AddToRoleAsync(user, IndustrialRoles.Admin);
        if (!assigned.Succeeded) return IdentityFailure(assigned, "roleAssignmentFailed");
        return Results.Created("/api/v1/users", new { user.Id, user.UserName, role = IndustrialRoles.Admin });
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, SignInManager<IndustrialSimUser> signInManager)
    {
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await signInManager.PasswordSignInAsync(request.UserName, request.Password, false, lockoutOnFailure: true);
        return result.Succeeded
            ? Results.Empty
            : IndustrialSimProblemDetails.Result(401, "Invalid credentials", "The supplied credentials are invalid.", "invalidCredentials");
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<IndustrialSimUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return IndustrialSimProblemDetails.Result(401, "Authentication required", "The current user is unavailable.", "authenticationRequired");
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? Results.NoContent() : IdentityFailure(result, "passwordChangeRejected");
    }

    private static async Task<IResult> SessionAsync(
        ClaimsPrincipal principal,
        IndustrialAuthOptions options,
        UserManager<IndustrialSimUser> userManager)
    {
        if (options.Mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            return Results.Ok(new { mode = options.Mode, authenticated = true, userName = "local-developer", roles = new[] { IndustrialRoles.Admin } });
        var user = await userManager.GetUserAsync(principal);
        return Results.Ok(new
        {
            mode = options.Mode,
            authenticated = user is not null,
            userName = user?.UserName,
            roles = user is null ? [] : await userManager.GetRolesAsync(user)
        });
    }

    private static async Task<IResult> ListUsersAsync(UserManager<IndustrialSimUser> userManager)
    {
        var values = new List<object>();
        foreach (var user in await userManager.Users.OrderBy(user => user.UserName).ToArrayAsync())
            values.Add(new { user.Id, user.UserName, roles = await userManager.GetRolesAsync(user) });
        return Results.Ok(values);
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        UserManager<IndustrialSimUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        if (!IndustrialRoles.All.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
            return IndustrialSimProblemDetails.Result(400, "Invalid role", $"Role '{request.Role}' is not supported.", "invalidRole");
        var canonicalRole = IndustrialRoles.All.Single(role => role.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
        if (!await roleManager.RoleExistsAsync(canonicalRole)) await roleManager.CreateAsync(new IdentityRole(canonicalRole));
        var user = new IndustrialSimUser { UserName = request.UserName };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded) return IdentityFailure(created, "userCreationRejected");
        var assigned = await userManager.AddToRoleAsync(user, canonicalRole);
        if (!assigned.Succeeded) return IdentityFailure(assigned, "roleAssignmentFailed");
        return Results.Created($"/api/v1/users/{Uri.EscapeDataString(request.UserName)}", new { user.Id, user.UserName, role = canonicalRole });
    }

    private static async Task<IResult> DeleteUserAsync(string userName, ClaimsPrincipal principal, UserManager<IndustrialSimUser> userManager)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null) return IndustrialSimProblemDetails.Result(404, "User not found", $"User '{userName}' was not found.", "userNotFound");
        if (principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id)
            return IndustrialSimProblemDetails.Result(409, "User conflict", "The current administrator cannot delete itself.", "cannotDeleteCurrentUser");
        var deleted = await userManager.DeleteAsync(user);
        return deleted.Succeeded ? Results.NoContent() : IdentityFailure(deleted, "userDeletionRejected");
    }

    private static async Task<IResult> UpsertSettingAsync(
        string key,
        UpdateSettingRequest request,
        ISettingCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase))
            return IndustrialSimProblemDetails.Result(400, "Secret setting rejected", "Secrets must use a secret provider, not the settings catalog.", "secretSettingRejected");
        var current = await repository.FindAsync(key, cancellationToken);
        await repository.UpsertAsync(new SettingCatalogItem(key, request.Value.GetRawText(), current?.Version ?? 0), cancellationToken);
        await db.CommitAsync(cancellationToken);
        return Results.Ok(new { key, value = request.Value });
    }

    private static IResult IdentityFailure(IdentityResult result, string errorCode) =>
        IndustrialSimProblemDetails.Result(400, "Identity operation rejected", string.Join(" ", result.Errors.Select(error => error.Description)), errorCode);
}
