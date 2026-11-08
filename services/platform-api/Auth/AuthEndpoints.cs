using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");
        group.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting("auth").WithName("RegisterUser");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("auth").WithName("LoginUser");
        group.MapGet("/me", GetCurrentUserAsync).RequireAuthorization().WithName("GetCurrentUser");
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        JwtTokenService tokenService)
    {
        var email = request.Email?.Trim();
        var displayName = request.DisplayName?.Trim();

        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["A valid email address is required."]
            });

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length is < 2 or > 120)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = ["Display name must be between 2 and 120 characters."]
            });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length is < 10 or > 128)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password must be between 10 and 128 characters."]
            });

        if (await userManager.FindByEmailAsync(email) is not null)
            return Results.Conflict(new { message = "An account with this email already exists." });

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return Results.ValidationProblem(createResult.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));

        const string travellerRole = "Traveller";
        if (!await roleManager.RoleExistsAsync(travellerRole))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = travellerRole });
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return Results.Problem("Could not initialize the default account role.", statusCode: 500);
            }
        }

        var addRoleResult = await userManager.AddToRoleAsync(user, travellerRole);
        if (!addRoleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return Results.Problem("Could not assign the default account role.", statusCode: 500);
        }

        return Results.Created("/api/v1/auth/me", await tokenService.CreateAsync(user));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        JwtTokenService tokenService)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { message = "Email and password are required." });

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Results.Unauthorized();

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Results.Unauthorized();

        return Results.Ok(await tokenService.CreateAsync(user));
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<User> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        if (!Guid.TryParse(userId, out var id))
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(new AuthUserResponse(user.Id, user.Email ?? "", user.DisplayName, roles.ToArray()));
    }
}
