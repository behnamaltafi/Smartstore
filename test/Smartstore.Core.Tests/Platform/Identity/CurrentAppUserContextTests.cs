using Microsoft.AspNetCore.Identity;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Smartstore.Core.Identity;

namespace Smartstore.Core.Tests.Platform.Identity;

[TestFixture]
public class CurrentAppUserContextTests
{
    [Test]
    public async Task AnonymousRequestDoesNotResolveAnAppUser()
    {
        var contextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var sut = new CurrentAppUserContext(contextAccessor, null);

        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task LegacyCustomerIdentifierDoesNotResolveAnAppUser()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "321") }, "LegacyCustomerCookie"))
        };

        var sut = new CurrentAppUserContext(
            new HttpContextAccessor { HttpContext = httpContext }, null);

        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task MalformedClaimIsDetectedAsAppUserSessionAndRejected()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(CurrentAppUserContext.UserGuidClaim, "malformed", ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer) }, IdentityConstants.ApplicationScheme))
        };
        var sut = new CurrentAppUserContext(
            new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task LegacyCustomerSessionDoesNotDeclareAppUserIdentity()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "321") }, "LegacyCustomerCookie"))
        };
        var sut = new CurrentAppUserContext(
            new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.False);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task MalformedAppUserClaimIsRejected()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(CurrentAppUserContext.UserGuidClaim, "malformed", ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer) }, IdentityConstants.ApplicationScheme))
        };

        var sut = new CurrentAppUserContext(
            new HttpContextAccessor { HttpContext = httpContext }, null);

        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }
    [Test]
    public async Task ExternalProviderClaimCannotImpersonateAppUser()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(CurrentAppUserContext.UserGuidClaim,
                        Guid.NewGuid().ToString("D"),
                        ClaimValueTypes.String,
                        CurrentAppUserContext.TrustedClaimIssuer)
                },
                IdentityConstants.ExternalScheme))
        };

        var sut = new CurrentAppUserContext(
            new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.False);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task ClaimedAppUserFromUntrustedIssuerCannotImpersonate()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(CurrentAppUserContext.UserGuidClaim,
                    Guid.NewGuid().ToString("D"), ClaimValueTypes.String, "ExternalIssuer") },
                IdentityConstants.ApplicationScheme))
        };

        var sut = new CurrentAppUserContext(
            new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.False);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }


    [Test]
    public async Task AppUserWithoutSignedLegacyLoginIdIsRejected()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(CurrentAppUserContext.UserGuidClaim,
                    Guid.NewGuid().ToString("D"), ClaimValueTypes.String,
                    CurrentAppUserContext.TrustedClaimIssuer) },
                IdentityConstants.ApplicationScheme))
        };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task DuplicateTrustedAppUserClaimsFailClosed()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(CurrentAppUserContext.UserGuidClaim, Guid.NewGuid().ToString("D"),
                        ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer),
                    new Claim(CurrentAppUserContext.UserGuidClaim, Guid.NewGuid().ToString("D"),
                        ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer),
                    new Claim(ClaimTypes.NameIdentifier, "10")
                }, IdentityConstants.ApplicationScheme))
        };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task InvalidLegacyLoginIdFailsBeforeDatabaseLookup()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(CurrentAppUserContext.UserGuidClaim, Guid.NewGuid().ToString("D"),
                        ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer),
                    new Claim(ClaimTypes.NameIdentifier, "not-an-id")
                }, IdentityConstants.ApplicationScheme))
        };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public void SignedLegacyBindingCanBeReadWithoutDatabaseAccess()
    {
        var guid = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(CurrentAppUserContext.UserGuidClaim, guid.ToString("D"),
                ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer),
            new Claim(ClaimTypes.NameIdentifier, "321")
        }, IdentityConstants.ApplicationScheme));

        Assert.That(CurrentAppUserContext.TryReadTrustedLegacyBinding(
            principal, out var resolvedGuid, out var loginId), Is.True);
        Assert.That(resolvedGuid, Is.EqualTo(guid));
        Assert.That(loginId, Is.EqualTo(321));
    }

    [Test]
    public async Task TwoTrustedAppUserIdentitiesAreRejected()
    {
        ClaimsIdentity CreateIdentity() => new(new[]
        {
            new Claim(CurrentAppUserContext.UserGuidClaim, Guid.NewGuid().ToString("D"),
                ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer),
            new Claim(ClaimTypes.NameIdentifier, "321")
        }, IdentityConstants.ApplicationScheme);

        var principal = new ClaimsPrincipal(new[] { CreateIdentity(), CreateIdentity() });
        var http = new DefaultHttpContext { User = principal };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = http }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(CurrentAppUserContext.TryReadTrustedLegacyBinding(
            principal, out _, out _), Is.False);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task IndependentCookieWithoutSecurityStampCannotResolveAppUser()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(AppUserAuthenticationDefaults.GuidClaim, Guid.NewGuid().ToString("D"),
                    ClaimValueTypes.String, AppUserAuthenticationDefaults.Issuer)
            }, AppUserAuthenticationDefaults.Scheme))
        };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task IndependentCookieWithDuplicateGuidsFailsClosed()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(AppUserAuthenticationDefaults.GuidClaim, Guid.NewGuid().ToString("D"),
                    ClaimValueTypes.String, AppUserAuthenticationDefaults.Issuer),
                new Claim(AppUserAuthenticationDefaults.GuidClaim, Guid.NewGuid().ToString("D"),
                    ClaimValueTypes.String, AppUserAuthenticationDefaults.Issuer),
                new Claim(AppUserAuthenticationDefaults.SecurityStampClaim, Guid.NewGuid().ToString("N"),
                    ClaimValueTypes.String, AppUserAuthenticationDefaults.Issuer)
            }, AppUserAuthenticationDefaults.Scheme))
        };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task IndependentCookieWithWrongIssuerCannotBeUsed()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(AppUserAuthenticationDefaults.GuidClaim, Guid.NewGuid().ToString("D"),
                    ClaimValueTypes.String, "untrusted"),
                new Claim(AppUserAuthenticationDefaults.SecurityStampClaim, "fake",
                    ClaimValueTypes.String, "untrusted")
            }, AppUserAuthenticationDefaults.Scheme))
        };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.False);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

    [Test]
    public async Task ValidLegacyBindingDoesNotAuthenticateAnAppUserAfterCutover()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(CurrentAppUserContext.UserGuidClaim, Guid.NewGuid().ToString("D"),
                ClaimValueTypes.String, CurrentAppUserContext.TrustedClaimIssuer),
            new Claim(ClaimTypes.NameIdentifier, "123")
        }, IdentityConstants.ApplicationScheme));
        var context = new DefaultHttpContext { User = principal };
        var sut = new CurrentAppUserContext(new HttpContextAccessor { HttpContext = context }, null);

        Assert.That(sut.IsAppUserIdentityPresent, Is.True);
        Assert.That(await sut.GetCurrentUserAsync(), Is.Null);
    }

}
