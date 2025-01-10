using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;

public static class Services
{
    public static void Configure(IServiceCollection services, IConfigurationRoot root)
    {
        services
            .AddSingleton(_ => GetConfig<OneDriveConfig>(root))
            .AddSingleton(_ => GetConfig<NetlifyConfig>(root))
            .AddSingleton<ManifestManager>()
            .AddSingleton<SiteBuilder>()
            .AddSingleton(provider =>
            {
                var config = provider.GetRequiredService<OneDriveConfig>();
                var credentials = new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
                return new GraphServiceClient(credentials);
            })
            .AddSingleton(provider =>
            {
                var graph = provider.GetRequiredService<GraphServiceClient>();
                var config = provider.GetRequiredService<OneDriveConfig>();
                return new OneDriveClient(graph, config.ShareUrl);
            })
            .AddSingleton(provider =>
            {
                var config = provider.GetRequiredService<NetlifyConfig>();
                return new NetlifySharp.NetlifyClient(config.AccessToken);
            })
            .AddSingleton<NetlifyClient>();
    }

    public static ServiceProvider CreateProvider(string? settingsFileName)
    {
        var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDataFileName = Path.Combine(appDataDir, "ansel", "appsettings.json");

        var builder = Host
            .CreateApplicationBuilder()
            .Configuration
            .AddJsonFile(appDataFileName, optional: true)
            .AddJsonFile("appsettings.json", optional: true);

        if (!string.IsNullOrWhiteSpace(settingsFileName))
        {
            builder.AddJsonFile(settingsFileName, optional: true);
        }

        builder.AddEnvironmentVariables();

        var config = builder.Build();

        var services = new ServiceCollection();
        Configure(services, config);

        return services.BuildServiceProvider();
    }

    static T GetConfig<T>(IConfigurationRoot root)
    {
        var name = typeof(T).Name;
        var section = root.GetRequiredSection(name)
            ?? throw new Exception($"failed to get configuration for {name}");

        var config = section.Get<T>()
            ?? throw new Exception($"failed to get configuration for {name}");

        return config;
    }
}