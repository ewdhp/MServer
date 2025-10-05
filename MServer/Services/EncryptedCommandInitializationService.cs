using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MServer.Services;

namespace MServer.Services
{
    public class EncryptedCommandInitializationService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EncryptedCommandInitializationService> _logger;

        public EncryptedCommandInitializationService(
            IServiceProvider serviceProvider,
            ILogger<EncryptedCommandInitializationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Initializing encrypted commands...");

                using var scope = _serviceProvider.CreateScope();
                var setupService = scope.ServiceProvider.GetRequiredService<EncryptedCommandSetup>();

                // Setup encrypted commands on startup
                await setupService.SetupGoogleAuthenticatorCommand();
                await setupService.SetupTotpCodeGenerationCommand();
                await setupService.SetupSystemAuthenticationCommand();

                _logger.LogInformation("✅ Encrypted commands initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize encrypted commands");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Encrypted command service stopping...");
            return Task.CompletedTask;
        }
    }
}