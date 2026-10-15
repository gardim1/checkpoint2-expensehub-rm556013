using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace ExpenseHub.Api.Auth;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/login", LoginAsync)
            .WithValidation<LoginRequest>()
            .AllowAnonymous();
        endpoints.MapPost("/register", RegisterAsync)
            .WithValidation<RegisterRequest>()
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(request.Email);
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        if (user is not null)
        {
            SignInResult result = await signInManager.PasswordSignInAsync(
                user, request.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                // SignInManager already wrote the Identity AccessTokenResponse.
                return Results.Empty;
            }
        }

        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Credenciais inválidas");
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, UserManager<IdentityUser> userManager)
    {
        var user = new IdentityUser { UserName = request.Email, Email = request.Email };
        IdentityResult result = await userManager.CreateAsync(user, request.Password);
        return result.Succeeded
            ? Results.Ok(new UserResponse(user.Id, user.Email, []))
            : result.ToValidationProblem();
    }
}
