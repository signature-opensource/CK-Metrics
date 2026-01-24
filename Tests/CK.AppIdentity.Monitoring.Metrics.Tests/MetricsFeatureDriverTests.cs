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
}
