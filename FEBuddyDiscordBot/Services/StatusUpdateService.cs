using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Services;
public class StatusUpdateService
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly IConfiguration _config;

    public bool IsRunning { get; protected set; } = false;

    private Thread _currentServiceThread;

    /// <summary>
    /// Constructor for the Role Assignment Service
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public StatusUpdateService(IServiceProvider services)
    {
        _services = services;
        _logger = _services.GetRequiredService<ILogger<StatusUpdateService>>();
        _config = _services.GetRequiredService<IConfiguration>();

        _logger.LogDebug("Loaded: StatusUpdateService");
    }

    public Task Start()
    {
        _logger.LogDebug("StatusUpdateService: Creating Thread for Uptime Kuma Status calls.");
        IsRunning = true;
        _currentServiceThread = new Thread(Run);
        _currentServiceThread.Start();

        return Task.CompletedTask;
    }

    private async void Run()
    {
        _logger.LogDebug("StatusUpdateService: Service for Uptime Kuma Status calls started.");

        string url = _config.GetRequiredSection("UptimeKumaUrl").Value;

        using HttpClient httpClient = new HttpClient();
        
        while (IsRunning)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                _logger.LogDebug("StatusUpdateService: Uptime-Kuma response '" + response.StatusCode + " - " + response.Content.ToString() + "'");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("StatusUpdateService: " + ex.Message);
            }
            _logger.LogDebug("StatusUpdateService: Waiting for 60 seconds.");
            Thread.Sleep(60000);
        }

    }


}
