using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Content.Server.Database;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Serilog;
using SS14.Admin.Helpers;
using SS14.Admin.SignIn;

namespace SS14.Admin
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<SignInManager>();
            services.AddScoped<LoginHandler>();
            services.AddScoped<BanHelper>();
            services.AddScoped<PlayerLocator>();
            services.AddHttpContextAccessor();

            var connStr = Configuration.GetConnectionString("DefaultConnection");
            if (connStr == null)
                throw new InvalidOperationException("Need to specify DefaultConnection connection string");

            services.AddDbContext<PostgresServerDbContext>(options => options.UseNpgsql(connStr));

            // Pirate: mirror the game's staff, logs and whitelist permissions.
            services.AddAuthorization(options =>
            {
                options.AddPolicy("PirateStaff", policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole("ADMIN", "MODERATOR", "PERMISSIONS", "HOST"));
                options.AddPolicy("PirateLogs", policy => policy
                    .RequireRole("ADMIN", "MODERATOR", "PERMISSIONS", "HOST")
                    .RequireRole("LOGS"));
                options.AddPolicy("PirateWhitelist", policy => policy
                    .RequireRole("ADMIN", "MODERATOR", "PERMISSIONS", "HOST")
                    .RequireRole("BAN"));
            });

            services.AddControllers();
            services.AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/Players", "PirateStaff");
                options.Conventions.AuthorizeFolder("/Connections", "PirateStaff");
                options.Conventions.AuthorizeFolder("/Bans", "PirateStaff");
                options.Conventions.AuthorizeFolder("/RoleBans", "PirateStaff");
                options.Conventions.AuthorizeFolder("/Logs", "PirateLogs");
                options.Conventions.AuthorizeFolder("/Characters", "PirateStaff");
                options.Conventions.AuthorizeFolder("/Whitelist", "PirateStaff");
                options.Conventions.AuthorizePage("/Whitelist/AddWhitelist", "PirateWhitelist");
            });

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Cookies";
                    options.DefaultChallengeScheme = "oidc";
                })
                .AddCookie("Cookies", options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromHours(1);
                })
                .AddOpenIdConnect("oidc", options =>
                {
                    options.SignInScheme = "Cookies";

                    options.Authority = Configuration["Auth:Authority"];
                    options.ClientId = Configuration["Auth:ClientId"];
                    options.ClientSecret = Configuration["Auth:ClientSecret"];
                    options.SaveTokens = true;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.GetClaimsFromUserInfoEndpoint = true;

                    options.Events.OnTokenValidated = async ctx =>
                    {
                        var handler = ctx.HttpContext.RequestServices.GetRequiredService<LoginHandler>();
                        await handler.HandleTokenValidated(ctx);
                    };
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseSerilogRequestLogging();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            var forwardedHeadersOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            };

            foreach (var entry in Configuration.GetSection("ForwardProxies").Get<string[]>() ?? Array.Empty<string>())
            {
                // Try to parse as CIDR notation first (e.g., 192.168.1.0/24)
                if (IPHelper.TryParseIpOrCidr(entry, out var parsed))
                {
                    var (ipAddress, prefixLength) = parsed;
                    if (prefixLength.HasValue)
                    {
                        // It's a CIDR subnet, add to KnownNetworks
                        var network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ipAddress, prefixLength.Value);
                        forwardedHeadersOptions.KnownNetworks.Add(network);
                    }
                    else
                    {
                        // It's a single IP address, add to KnownProxies
                        forwardedHeadersOptions.KnownProxies.Add(ipAddress);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Invalid IP address or CIDR notation in ForwardProxies: {entry}");
                }
            }

            app.UseForwardedHeaders(forwardedHeadersOptions);

            var pathBase = Configuration.GetValue<string>("PathBase");
            if (!string.IsNullOrEmpty(pathBase))
            {
                app.UsePathBase(pathBase);
            }

            app.UseAuthentication();
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
            });
        }
    }
}
