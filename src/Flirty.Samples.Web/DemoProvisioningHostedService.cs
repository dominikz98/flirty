using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flirty.Samples.Web;

/// <summary>
/// Builds the demo dialog at app startup. Implements <see cref="IHostedLifecycleService"/> and
/// uses <see cref="StartedAsync"/> – this point lies <em>after</em> the start of all hosted services
/// (incl. Kestrel and the auto-migration), so that the app can already reach its own admin endpoints over
/// HTTP and the schema exists. Because <see cref="StartedAsync"/> is awaited by the host, the
/// dialog is deterministically present after startup (important for the E2E tests).
/// </summary>
public sealed class DemoProvisioningHostedService : IHostedLifecycleService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<DemoProvisioningHostedService> _logger;

    /// <summary>Initializes the service with the dependencies needed for provisioning.</summary>
    /// <param name="httpClientFactory">Factory for the admin client pointing at this app.</param>
    /// <param name="services">Service provider for the <see cref="Flirty.Persistence.FlirtyDbContext"/> scope.</param>
    /// <param name="logger">Logger for the provisioning result.</param>
    public DemoProvisioningHostedService(
        IHttpClientFactory httpClientFactory, IServiceProvider services, ILogger<DemoProvisioningHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _services = services;
        _logger = logger;
    }

    /// <summary>Runs the provisioning after the host (incl. Kestrel) has started.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the completion of the provisioning.</returns>
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(WebSampleApp.AdminHttpClientName);
        await DemoDialogProvisioner.EnsureProvisionedAsync(client, _services, _logger, cancellationToken);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
