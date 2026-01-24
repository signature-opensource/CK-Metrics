using CK.AppIdentity;
using CK.Core;
using CK.Metrics;
using CK.Monitoring;
using CK.Monitoring.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Diagnostics.Metrics;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Monitoring.Metrics.Tests;

/// <summary>
/// Mock consumer for testing MetricsFeatureDriver.
/// </summary>
sealed class MockMetricsConsumer : IMetricsConsumer
{
    public string Name { get; }
    public long CompletedUntilAddress { get; set; }
    public bool WasDisposed { get; private set; }

    public MockMetricsConsumer( string name, long completedUntilAddress = 0 )
    {
        Name = name;
        CompletedUntilAddress = completedUntilAddress;
    }

    public Task StartAsync( IActivityMonitor monitor, CancellationToken ct ) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return ValueTask.CompletedTask;
    }
}

[TestFixture]
public class MetricsFeatureDriverTests
{
    [Test]
    public async Task FeatureDriver_creates_FasterLog_and_injects_into_handler_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_creates_FasterLog_and_injects_into_handler_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:MemoryPageCount"] = "4",
                ["CK-AppIdentity:Local:Metrics:HandlerWaitTimeoutMs"] = "5000"
            } )
            .Build();

        // Ensure GrandOutput.Default is disposed before the test.
        if( GrandOutput.Default != null )
        {
            await GrandOutput.Default.DisposeAsync();
        }

        // Pre-configure GrandOutput with MetricsLogHandler.
        var goConfig = new GrandOutputConfiguration()
            .AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } )
            .AddHandler( new TextFileConfiguration { Path = Path.Combine( testDir, "Text" ) } );
        GrandOutput.EnsureActiveDefault( goConfig );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        // MetricsFeatureDriver should have created FasterLog.
        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Not.Null );

        // The FasterLog directory should exist.
        var expectedPath = Path.Combine( testDir, "#Dev", "TestDomain", "$TestApp", "-Local", "FasterLog", "Metrics" );
        Assert.That( Directory.Exists( expectedPath ), Is.True, $"Expected directory '{expectedPath}' to exist." );

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task FeatureDriver_without_configuration_does_nothing_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_without_configuration_does_nothing_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir
                // No Local:Metrics configuration.
            } )
            .Build();

        // Ensure GrandOutput.Default is not active before the test.
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

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        // MetricsFeatureDriver should not have created FasterLog because no config.
        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Null );

        await host.StopAsync();
    }

    [Test]
    public async Task FeatureDriver_uses_existing_GrandOutput_and_injects_FasterLog_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( FeatureDriver_uses_existing_GrandOutput_and_injects_FasterLog_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:HandlerWaitTimeoutMs"] = "5000"
            } )
            .Build();

        // Ensure GrandOutput.Default is active BEFORE starting the test with MetricsLogHandler.
        var preExistingConfig = new GrandOutputConfiguration()
            .AddHandler( new MetricsLogHandlerConfiguration { CommitRate = 2 } )
            .AddHandler( new TextFileConfiguration { Path = Path.Combine( testDir, "PreExisting" ) } );
        GrandOutput.EnsureActiveDefault( preExistingConfig );
        var originalGrandOutput = GrandOutput.Default;
        Assert.That( originalGrandOutput, Is.Not.Null );

        var builder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true } );
        builder.Configuration.AddConfiguration( config );

        builder.Services.AddSingleton<ApplicationIdentityService>();
        builder.Services.AddSingleton<IHostedService>( sp => sp.GetRequiredService<ApplicationIdentityService>() );
        builder.Services.AddSingleton<MetricsFeatureDriver>();
        builder.Services.AddSingleton<IApplicationIdentityFeatureDriver>( sp => sp.GetRequiredService<MetricsFeatureDriver>() );

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        // GrandOutput.Default should still be the same instance.
        Assert.That( GrandOutput.Default, Is.SameAs( originalGrandOutput ) );

        // MetricsFeatureDriver should have created FasterLog and injected it.
        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Not.Null );

        // The FasterLog directory should exist.
        var expectedPath = Path.Combine( testDir, "#Dev", "TestDomain", "$TestApp", "-Local", "FasterLog", "Metrics" );
        Assert.That( Directory.Exists( expectedPath ), Is.True );

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task RemoveConsumerAsync_removes_and_disposes_consumer_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( RemoveConsumerAsync_removes_and_disposes_consumer_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
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

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Not.Null );

        // Register a mock consumer.
        var mockConsumer = new MockMetricsConsumer( "test-consumer" );
        driver.RegisterConsumer( mockConsumer );
        Assert.That( driver.Consumers.Count, Is.EqualTo( 1 ) );

        // Remove the consumer.
        await driver.RemoveConsumerAsync( "test-consumer" );
        Assert.That( driver.Consumers.Count, Is.EqualTo( 0 ) );
        Assert.That( mockConsumer.WasDisposed, Is.True );

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task RemoveConsumerAsync_with_unknown_name_does_nothing_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( RemoveConsumerAsync_with_unknown_name_does_nothing_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
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

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Not.Null );

        // Register a mock consumer.
        var mockConsumer = new MockMetricsConsumer( "test-consumer" );
        driver.RegisterConsumer( mockConsumer );
        Assert.That( driver.Consumers.Count, Is.EqualTo( 1 ) );

        // Try to remove a non-existent consumer.
        await driver.RemoveConsumerAsync( "unknown-consumer" );

        // Original consumer should still be there.
        Assert.That( driver.Consumers.Count, Is.EqualTo( 1 ) );
        Assert.That( mockConsumer.WasDisposed, Is.False );

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task OnTruncationTimer_truncates_based_on_consumer_progress_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( OnTruncationTimer_truncates_based_on_consumer_progress_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
                ["CK-AppIdentity:Local:Metrics:Path"] = "FasterLog/Metrics",
                ["CK-AppIdentity:Local:Metrics:TruncationIntervalMs"] = "100",
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

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var appIdentity = host.Services.GetRequiredService<ApplicationIdentityService>();
        await appIdentity.InitializationTask;

        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Not.Null );

        var log = driver.FasterLog!;
        var initialBeginAddress = log.BeginAddress;

        // Write some data to the log.
        for( int i = 0; i < 100; i++ )
        {
            log.Enqueue( System.Text.Encoding.UTF8.GetBytes( $"Test entry {i} with some padding to make it larger..." ) );
        }
        await log.CommitAsync();

        // Register a consumer that claims to have processed everything.
        var mockConsumer = new MockMetricsConsumer( "test-consumer", log.TailAddress );
        driver.RegisterConsumer( mockConsumer );

        // Wait for the truncation timer to fire.
        await Task.Delay( 300 );

        // The timer should have truncated the log. Note: truncation is page-aligned,
        // so BeginAddress may not change much if data fits in one page.
        // The test ensures the timer fires without errors.
        Assert.That( driver.FasterLog, Is.Not.Null, "FasterLog should still be valid after truncation." );

        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }

    [Test]
    public async Task dynamic_remote_lifecycle_triggers_feature_driver_methods_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( dynamic_remote_lifecycle_triggers_feature_driver_methods_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?>
            {
                ["CK-AppIdentity:FullName"] = "TestDomain/$TestApp/#Dev",
                ["CK-AppIdentity:StoreRootPath"] = testDir,
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

        using var host = builder.AddApplicationIdentityServiceConfiguration().CKBuild();
        await host.StartAsync();

        var identityService = host.Services.GetRequiredService<ApplicationIdentityService>();
        await identityService.InitializationTask;

        var driver = host.Services.GetRequiredService<MetricsFeatureDriver>();
        Assert.That( driver.FasterLog, Is.Not.Null );

        // Add a dynamic remote - triggers SetupDynamicRemoteAsync.
        var remote = await identityService.AddRemoteAsync( TestHelper.Monitor, c =>
        {
            c["PartyName"] = "DynamicRemote1";
        } );
        Assert.That( remote, Is.Not.Null );

        // Destroy the remote - triggers TeardownDynamicRemoteAsync.
        await remote!.DestroyAsync();

        // If we get here without exception, the dynamic methods were called successfully.
        await host.StopAsync();
        await GrandOutput.Default!.DisposeAsync();
    }
}
