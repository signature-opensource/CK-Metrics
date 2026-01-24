using CK.AppIdentity;
using CK.AppIdentity.Monitoring.Metrics;
using CK.Core;
using CK.Monitoring;
using CK.Monitoring.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Monitoring.Metrics.Csv.Tests;

[TestFixture]
public class CsvMetricsConsumerFeatureDriverTests
{
    [Test]
    public async Task FeatureDriver_creates_consumer_and_registers_with_MetricsFeatureDriver_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_creates_consumer_and_registers_with_MetricsFeatureDriver_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                // MetricsFeatureDriver configuration
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:TruncationIntervalMs"] = "0",
                ["CK-AppIdentity:Local:Metrics:HandlerWaitTimeoutMs"] = "5000",
                // CsvMetricsConsumerFeatureDriver configuration
                ["CK-AppIdentity:Local:MetricsCsv:Name"] = "test-csv",
                ["CK-AppIdentity:Local:MetricsCsv:Path"] = "Exports/metrics.csv"
            } )
            .Build();

        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }

        var goConfig = new GrandOutputConfiguration()
            .AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } );
        GrandOutput.EnsureActiveDefault( goConfig );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );
        builder.Services.AddSingleton<CsvMetricsConsumerFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<CsvMetricsConsumerFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var metricsDriver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( metricsDriver.FasterLog, Is.Not.Null );

        // The CsvMetricsConsumerFeatureDriver should have registered a consumer.
        Assert.That( metricsDriver.Consumers.Count, Is.EqualTo( 1 ) );
        Assert.That( metricsDriver.Consumers[0].Name, Is.EqualTo( "test-csv" ) );

        // Note: The CSV file is only created when data is written to it.
        // For this test, we just verify the consumer was registered correctly.

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task FeatureDriver_without_MetricsCsv_config_does_not_create_consumer_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_without_MetricsCsv_config_does_not_create_consumer_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                // MetricsFeatureDriver configuration only - no MetricsCsv.
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:TruncationIntervalMs"] = "0",
                ["CK-AppIdentity:Local:Metrics:HandlerWaitTimeoutMs"] = "5000"
            } )
            .Build();

        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }

        var goConfig = new GrandOutputConfiguration()
            .AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } );
        GrandOutput.EnsureActiveDefault( goConfig );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );
        builder.Services.AddSingleton<CsvMetricsConsumerFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<CsvMetricsConsumerFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var metricsDriver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( metricsDriver.FasterLog, Is.Not.Null );

        // No consumer should have been created.
        Assert.That( metricsDriver.Consumers.Count, Is.EqualTo( 0 ) );

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task FeatureDriver_without_FasterLog_does_not_create_consumer_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_without_FasterLog_does_not_create_consumer_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                // No Metrics configuration - so FasterLog will be null.
                // But provide MetricsCsv config to ensure the driver tries to create a consumer.
                ["CK-AppIdentity:Local:MetricsCsv:Name"] = "test-csv",
                ["CK-AppIdentity:Local:MetricsCsv:Path"] = "Exports/metrics.csv"
            } )
            .Build();

        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );
        builder.Services.AddSingleton<CsvMetricsConsumerFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<CsvMetricsConsumerFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var metricsDriver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        // No Metrics config means no FasterLog.
        Assert.That( metricsDriver.FasterLog, Is.Null );
        // Therefore no consumer.
        Assert.That( metricsDriver.Consumers.Count, Is.EqualTo( 0 ) );

        await host.StopAsync();
    }

    [Test]
    public async Task FeatureDriver_TeardownAsync_removes_consumer_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_TeardownAsync_removes_consumer_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:TruncationIntervalMs"] = "0",
                ["CK-AppIdentity:Local:Metrics:HandlerWaitTimeoutMs"] = "5000",
                ["CK-AppIdentity:Local:MetricsCsv:Name"] = "test-csv",
                ["CK-AppIdentity:Local:MetricsCsv:Path"] = "Exports/metrics.csv"
            } )
            .Build();

        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }

        var goConfig = new GrandOutputConfiguration()
            .AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } );
        GrandOutput.EnsureActiveDefault( goConfig );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );
        builder.Services.AddSingleton<CsvMetricsConsumerFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<CsvMetricsConsumerFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var metricsDriver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( metricsDriver.Consumers.Count, Is.EqualTo( 1 ) );

        // Stop the host - this triggers TeardownAsync.
        await host.StopAsync();

        // After teardown, consumer should be removed.
        Assert.That( metricsDriver.Consumers.Count, Is.EqualTo( 0 ) );

        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task FeatureDriver_dynamic_remote_lifecycle_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_dynamic_remote_lifecycle_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:TruncationIntervalMs"] = "0",
                ["CK-AppIdentity:Local:Metrics:HandlerWaitTimeoutMs"] = "5000",
                ["CK-AppIdentity:Local:MetricsCsv:Name"] = "test-csv",
                ["CK-AppIdentity:Local:MetricsCsv:Path"] = "Exports/metrics.csv"
            } )
            .Build();

        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }

        var goConfig = new GrandOutputConfiguration()
            .AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } );
        GrandOutput.EnsureActiveDefault( goConfig );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );
        builder.Services.AddSingleton<CsvMetricsConsumerFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<CsvMetricsConsumerFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var identityService = host.Services.GetRequiredService<ApplicationIdentityService>();
        await identityService.InitializationTask;

        // Add a dynamic remote - triggers SetupDynamicRemoteAsync on all feature drivers.
        var remote = await identityService.AddRemoteAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "DynamicRemote1";
        } );
        Assert.That( remote, Is.Not.Null );

        // Destroy the remote - triggers TeardownDynamicRemoteAsync on all feature drivers.
        await remote!.DestroyAsync();

        // If we reach here without exception, the dynamic methods were called successfully.
        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }
}
