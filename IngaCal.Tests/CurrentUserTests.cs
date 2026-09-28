using System.Security.Claims;
using IngaCal.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace IngaCal.Tests;

public sealed class CurrentUserTests
{
    [Fact]
    public async Task GetId_AuthenticatedPrincipal_UsesStableIdentifierClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "account-id"), new(ClaimTypes.Name, "display-name")], "test"));
        Assert.Equal("account-id", await new CurrentUser(new FixedAuthentication(user)).GetIdAsync());
    }

    [Fact]
    public async Task GetId_AnonymousPrincipalWithIdentifier_StillRequiresSignIn()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "untrusted") ]));
        var error = await Assert.ThrowsAsync<JournalException>(() => new CurrentUser(new FixedAuthentication(user)).GetIdAsync());
        Assert.Contains("session has ended", error.Message);
    }

    [Fact]
    public async Task GetId_AuthenticatedWithoutIdentifier_RequiresSignIn()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, "display-name")], "test"));
        var error = await Assert.ThrowsAsync<JournalException>(() => new CurrentUser(new FixedAuthentication(user)).GetIdAsync());
        Assert.Equal("Please sign in again.", error.Message);
    }

    private sealed class FixedAuthentication(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
