using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using MServer.Middleware;
using MServer.Services;
using System.Text;
using MServer.Data;
using System.Net.Http;
using System;
using MServer.Services.Auth;

namespace MServer
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            // Replace the default configuration with one that uses "appSettings.json"
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appSettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Register services
            services.AddDbContext<UDbContext>(options =>
                options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection")));

            services.AddScoped<JwtTokenService>();
            services.AddSingleton<FingerprintService>();

            // Register TokenValidationService
            services.AddSingleton<TokenValidationService>();

            services.AddSingleton<TokenManagementService>();
            services.AddSingleton<AuditLoggingService>();
            services.AddSingleton<ErrorHandlingService>();
            services.AddSingleton<RBACService>();
            services.AddSingleton<SshService>();
            services.AddSingleton<GraphExecutor>();
            services.AddSingleton<StatePersistenceService>();
            services.AddSingleton<WebSocketMessageHandler>();
            services.AddSingleton<IAuditLoggingService, AuditLoggingService>();
            // Register AuthModule
            services.AddSingleton<AuthModule>();

            // Register IJwtTokenService
            services.AddScoped<IJwtTokenService, JwtTokenService>();

            // Register IUCService and IHttpContextAccessor
            services.AddHttpContextAccessor();
            services.AddSingleton<IUCService, UserContextService>();

            // Register HttpClient
            services.AddHttpClient();

            // Configure CORS to allow requests from the frontend
            services.AddCors(options =>
            {
                options.AddPolicy("AllowFrontend",
                    builder =>
                    {
                        builder.WithOrigins("http://localhost:3000") // Allow requests from React frontend
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials(); // Allow cookies and credentials
                    });
            });

            services.AddControllers();

            var jwtKey = Configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
            {
                throw new ArgumentNullException("Jwt:Key", "JWT Key is not configured.");
            }
            var key = Encoding.ASCII.GetBytes(jwtKey);

            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = Configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Jwt:Key"])),
                    ClockSkew = TimeSpan.Zero // Disable clock skew
                };

                x.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("UserPolicy", policy => policy.RequireClaim("UserId"));
            });

            // Register FileWatcherService and CleanupBackgroundService
            services.AddSingleton<FileWatcherService>();
            services.AddHostedService<CleanupBackgroundService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            // Use CORS policy
            app.UseCors("AllowFrontend");

            // Enable WebSocket support
            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120)
            };
            app.UseWebSockets(webSocketOptions);

            // Add JWT authentication middleware
            app.UseAuthentication();
            app.UseAuthorization();

            // Add UnifiedAuth middleware for WebSocket requests
            app.UseMiddleware<UnifiedAuth>();

            // Add other middlewares as needed
            app.UseMiddleware<JwtMiddleware>();
            app.UseMiddleware<UserRequestLockMiddleware>();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}