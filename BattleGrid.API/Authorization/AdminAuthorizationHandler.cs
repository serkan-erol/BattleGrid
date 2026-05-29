using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace BattleGrid.API.Authorization;

public sealed class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IUserServices _userServices;

    public AdminAuthorizationHandler(IUserServices userServices)
    {
        _userServices = userServices;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRequirement requirement)
    {
        if (!context.User.TryGetAuthenticatedUserId(out var userId))
            return;

        if (await _userServices.IsAdminAsync(userId))
            context.Succeed(requirement);
    }
}
