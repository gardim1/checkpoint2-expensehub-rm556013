using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Security;

internal static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        RoleManager<IdentityRole> roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        UserManager<IdentityUser> userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(IdentitySeeder));

        foreach (string role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
            }
        }

        string? email = configuration["SeedAdmin:Email"];
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning("SeedAdmin:Email não configurado; nenhuma conta Admin criada.");
            return;
        }

        IdentityUser? admin = await userManager.FindByEmailAsync(email);
        if (admin is not null)
        {
            if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
            {
                logger.LogWarning("SeedAdmin:Email pertence a um usuário preexistente sem Admin; nenhuma promoção realizada.");
            }

            return;
        }

        string? password = configuration["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("SeedAdmin:Password não configurado; nenhuma conta Admin criada.");
            return;
        }

        admin = new IdentityUser { UserName = email, Email = email };
        EnsureSucceeded(await userManager.CreateAsync(admin, password));
        EnsureSucceeded(await userManager.AddToRoleAsync(admin, AppRoles.Admin));
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}
