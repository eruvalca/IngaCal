using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace IngaCal.Services;

public interface ICurrentUser
{
    Task<string> GetIdAsync();
}

public sealed class CurrentUser(AuthenticationStateProvider authentication) : ICurrentUser
{
    public async Task<string> GetIdAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true)
            throw new JournalException("Your session has ended. Please sign in again.");
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new JournalException("Please sign in again.");
    }
}
