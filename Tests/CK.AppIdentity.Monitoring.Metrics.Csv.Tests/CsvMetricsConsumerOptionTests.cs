using CK.AppIdentity;
using CK.AppIdentity.Monitoring.Metrics;
using CK.Core;
using CK.Testing;
using CK.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Monitoring.Metrics.Csv.Tests;

/// <summary>
/// The "Csv" option of the "Metrics" feature: the <see cref="MetricsFeatureDriver"/> creates, registers and starts its
/// consumer once its FasterLog is ready, and disposes it on teardown.
/// </summary>
[TestFixture]
public class CsvMetricsConsumerOptionTests
{
    // A host with the Metrics feature and the Csv option; the keys are the ones of "CK-AppIdentity:Local".
    static async Task<IHost> StartAsync( string testName, Dictionary<string, string?> local )
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", testName );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        var values = new Dictionary<string, string?>
        {
            ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
            ["CK-AppIdentity:StoreRootPath"] = testDir
        };
        foreach( var (k, v) in local ) values[$"CK-AppIdentity:Local:{k}"] = v;

        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }
        GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration().AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } ) );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( new ConfigurationBuilder().AddInMemoryCollection( values ).Build() );
        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );
        builder.Services.AddPartyComponentDirectory();
        builder.Services.AddPartyComponentOptions( typeof( CsvMetricsConsumerOption ) );
        var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();
        return host;
    }

    static Dictionary<string, string?> Metrics( params (string Key, string Value)[] csv )
    {
        var d = new Dictionary<string, string?>
        {
            ["Metrics:Path"] = "FasterLog/Metrics",
            ["Metrics:TruncationIntervalMs"] = "0",
            ["Metrics:HandlerWaitTimeoutMs"] = "5000"
        };
        foreach( var (k, v) in csv ) d[$"Metrics:Csv:{k}"] = v;
        return d;
    }

    [Test]
    public async Task the_option_creates_and_registers_its_consumer_and_the_teardown_disposes_it_Async()
    {
        using var host = await StartAsync( nameof( the_option_creates_and_registers_its_consumer_and_the_teardown_disposes_it_Async ),
                                           Metrics( ("Name", "test-csv"), ("Path", "Exports/metrics.csv") ) );
        await host.Services.GetRequiredService<ApplicationIdentityService>().InitializationTask;
        var metrics = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( metrics.FasterLog, Is.Not.Null );
        Assert.That( metrics.Consumers.Select( c => c.Name ), Is.EqualTo( new[] { "test-csv" } ) );
        await host.StopAsync();
        Assert.That( metrics.Consumers, Is.Empty );
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task without_the_option_there_is_no_consumer_Async()
    {
        using var host = await StartAsync( nameof( without_the_option_there_is_no_consumer_Async ), Metrics() );
        await host.Services.GetRequiredService<ApplicationIdentityService>().InitializationTask;
        var metrics = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( metrics.FasterLog, Is.Not.Null );
        Assert.That( metrics.Consumers, Is.Empty );
        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task without_the_Metrics_feature_there_is_nothing_Async()
    {
        using var host = await StartAsync( nameof( without_the_Metrics_feature_there_is_nothing_Async ), [] );
        await host.Services.GetRequiredService<ApplicationIdentityService>().InitializationTask;
        var metrics = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( metrics.FasterLog, Is.Null );
        Assert.That( metrics.Consumers, Is.Empty );
        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task an_invalid_configuration_fails_the_setup_Async()
    {
        // The model of the option is read before the setup of the feature: the host doesn't start.
        Assert.ThrowsAsync<CKException>( () => StartAsync( nameof( an_invalid_configuration_fails_the_setup_Async ),
                                                           Metrics( ("Name", "a-name-of-more-than-twenty-characters") ) ) );
        if( GrandOutput.Default != null ) await GrandOutput.Default.DisposeAsync();
    }
}
