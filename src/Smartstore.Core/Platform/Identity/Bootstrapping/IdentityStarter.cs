using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Smartstore.Core.DataExchange;
using Smartstore.Core.DataExchange.Import;
using Smartstore.Core.Identity;
using Smartstore.Core.Identity.Rules;
using Smartstore.Core.Localization;
using Smartstore.Core.Rules.Rendering;
using Smartstore.Core.Security;
using Smartstore.Core.Seo.Routing;
using Smartstore.Engine.Builders;
using Smartstore.Net;

namespace Smartstore.Core.Bootstrapping;

internal sealed class IdentityStarter : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.AddHttpClient();
        services.AddIdentity<Customer, CustomerRole>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
            .AddRoleManager<RoleManager<CustomerRole>>()
            .AddUserValidator<UserValidator>()
            .AddPasswordValidator<UserValidator>()
            .AddSignInManager<SmartSignInManager>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<IdentityOptions>, IdentityOptionsConfigurer>());

        if (appContext.IsInstalled)
        {
            services.ConfigureApplicationCookie(options =>
            {
                var ctx = appContext.Services.Resolve<IHttpContextAccessor>()?.HttpContext;

                options.Cookie.Name = CookieNames.Identity;
                options.LoginPath = RouteHelper.NormalizePathComponent("/login");
                options.LogoutPath = RouteHelper.NormalizePathComponent("/logout");
                options.AccessDeniedPath = RouteHelper.NormalizePathComponent("/access-denied");
                options.ReturnUrlParameter = "returnUrl";

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    var req = context.Request;
                    var returnUrl = (req.PathBase + req.Path + req.QueryString).NullEmpty();

                    throw new AccessDeniedException(null, returnUrl);
                };
            });

            services.ConfigureExternalCookie(options =>
            {
                options.Cookie.Name = CookieNames.ExternalAuthentication;
                options.LoginPath = RouteHelper.NormalizePathComponent("/login");
                options.LogoutPath = RouteHelper.NormalizePathComponent("/logout");
                options.AccessDeniedPath = RouteHelper.NormalizePathComponent("/access-denied");
                options.ReturnUrlParameter = "returnUrl";
            });

            services.AddAuthentication().AddCookie(CookieNames.ExternalAuthentication);

            // Separate AppUser authentication: never ask Identity<Customer>
            // to create, validate, or refresh an independent user's cookie.
            services.AddAuthentication().AddCookie(AppUserAuthenticationDefaults.Scheme, options =>
            {
                options.Cookie.Name = "Smartstore.AppUser";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = async ctx =>
                {
                    var identities = ctx.Principal?.Identities
                        .Where(x => x.IsAuthenticated &&
                            x.AuthenticationType == AppUserAuthenticationDefaults.Scheme).ToArray() ?? [];
                    if (identities.Length != 1)
                    {
                        ctx.RejectPrincipal();
                        return;
                    }

                    var identity = identities[0];
                    var guids = identity.Claims.Where(x =>
                        x.Type == AppUserAuthenticationDefaults.GuidClaim &&
                        x.Issuer == AppUserAuthenticationDefaults.Issuer).ToArray();
                    var stamps = identity.Claims.Where(x =>
                        x.Type == AppUserAuthenticationDefaults.SecurityStampClaim &&
                        x.Issuer == AppUserAuthenticationDefaults.Issuer).ToArray();
                    if (guids.Length != 1 || stamps.Length != 1 ||
                        !Guid.TryParse(guids[0].Value, out var userGuid) ||
                        userGuid == Guid.Empty || string.IsNullOrEmpty(stamps[0].Value))
                    {
                        ctx.RejectPrincipal();
                        return;
                    }

                    var db = ctx.HttpContext.RequestServices.GetRequiredService<SmartDbContext>();
                    var count = await db.AppUsers.AsNoTracking()
                        .Where(x => x.Active && x.UserGuid == userGuid &&
                            x.SecurityStamp == stamps[0].Value)
                        .Take(2).CountAsync();
                    if (count != 1)
                        ctx.RejectPrincipal();
                };
            });

            services.Configure<LocalizedEntityOptions>(o =>
            {
                o.Delegates.Add(LocalizedCookieInfoLoader.LoadLocalizedCookieInfos);
            });
        }

        // TODO: (mh) (core) // Add Identity IEmailSender and ISmsSender to service collection.
        // RE: This won't be needed right now. Will be implemented when we offer real 2FA.
    }

    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<UserStore>().As<IUserStore>().As<IUserStore<Customer>>().InstancePerLifetimeScope();
        builder.RegisterType<RoleStore>().As<IRoleStore>().As<IRoleStore<CustomerRole>>().InstancePerLifetimeScope();
        builder.RegisterType<CustomerService>().As<ICustomerService>().InstancePerLifetimeScope();
        builder.RegisterType<AppUserCustomerService>().As<IAppUserCustomerService>().InstancePerLifetimeScope();
        builder.RegisterType<AppUserBuyerContext>().As<IAppUserBuyerContext>().InstancePerLifetimeScope();
        builder.RegisterType<CurrentAppUserContext>().As<ICurrentAppUserContext>().InstancePerLifetimeScope();
        builder.RegisterType<AppUserCredentialService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ConfiguredAppUserChallengeSender>().As<IAppUserChallengeSender>().InstancePerLifetimeScope();
        builder.RegisterType<AppUserOrderAccessService>().As<IAppUserOrderAccessService>().InstancePerLifetimeScope();
        builder.RegisterType<AppUserProfileService>().As<IAppUserProfileService>().InstancePerLifetimeScope();
        builder.RegisterType<AppUserAddressBookService>().As<IAppUserAddressBookService>().InstancePerLifetimeScope();
        builder.RegisterType<VoidLookupNormalizer>().As<ILookupNormalizer>().InstancePerLifetimeScope();
        builder.RegisterType<PasswordHasher>().As<IPasswordHasher<Customer>>().InstancePerLifetimeScope();
        //builder.RegisterType<UserValidator>().As<IUserValidator<Customer>>().As<IPasswordValidator<Customer>>().InstancePerLifetimeScope();
        builder.RegisterType<GdprTool>().As<IGdprTool>().InstancePerLifetimeScope();
        builder.RegisterType<CookieConsentManager>().As<ICookieConsentManager>().InstancePerLifetimeScope();
        builder.RegisterType<CustomerImporter>().Keyed<IEntityImporter>(ImportEntityType.Customer).InstancePerLifetimeScope();

        // Rules.
        builder.RegisterType<TargetGroupService>().As<ITargetGroupService>().InstancePerLifetimeScope();
        builder.RegisterType<CustomerRoleRuleOptionsProvider>().As<IRuleOptionsProvider>().InstancePerLifetimeScope();
    }

    public override void BuildPipeline(RequestPipelineBuilder builder)
    {
        if (builder.ApplicationContext.IsInstalled)
        {
            builder.Configure(StarterOrdering.AuthMiddleware, app =>
            {
                app.UseAuthentication();

                // A verified AppUser cookie becomes the active HTTP principal.
                // The legacy ASP.NET Identity<Customer> cookie remains isolated.
                app.Use(async (http, next) =>
                {
                    var appUser = await http.AuthenticateAsync(AppUserAuthenticationDefaults.Scheme);
                    if (appUser.Succeeded && appUser.Principal is not null)
                        http.User = appUser.Principal;
                    await next();
                });
            });

            builder.Configure(StarterOrdering.AfterAuthMiddleware, app =>
            {
                app.UseAuthorization();
            });
        }
    }
}