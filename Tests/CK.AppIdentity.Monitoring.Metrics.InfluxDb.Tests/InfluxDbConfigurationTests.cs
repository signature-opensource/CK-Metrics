using CK.Core;
using NUnit.Framework;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb.Tests;

/// <summary>
/// <see cref="InfluxDbConfiguration"/> is the model of the "InfluxDb" option of the "Metrics" feature.
/// </summary>
[TestFixture]
public class InfluxDbConfigurationTests
{
    static UserMessageCollector Collector() => new UserMessageCollector( new CurrentCultureInfo( new TranslationService(), NormalizedCultureInfo.CodeDefault ) );

    static InfluxDbConfiguration? Read( string json, UserMessageCollector collector )
    {
        ConfigurationModelReader.TryRead<InfluxDbConfiguration>( ImmutableConfigurationSection.CreateFromJson( "Metrics", "InfluxDb", json ),
                                                                 "Metrics:InfluxDb",
                                                                 collector,
                                                                 out var c );
        return c;
    }

    [Test]
    public void the_model_reads_the_option()
    {
        var c = Read( """{ "ServerUrl": "http://localhost:8086", "Org": "o", "Bucket": "b", "Token": "t", "Tags": { "host": "h" }, "FlushIntervalMs": 50 }""", Collector() );
        Assert.That( c, Is.Not.Null );
        Assert.That( c!.Name, Is.EqualTo( InfluxDbConfiguration.DefaultConsumerName ) );
        Assert.That( c.Token, Is.EqualTo( "t" ) );
        Assert.That( c.Tags, Is.EqualTo( new Dictionary<string, string> { ["host"] = "h" } ) );
        Assert.That( c.FlushIntervalMs, Is.EqualTo( 50 ) );
        Assert.That( c.UseGzip, Is.True, "The default." );
    }

    [TestCase( """{ "Org": "o", "Bucket": "b" }""", "ServerUrl" )]
    [TestCase( """{ "ServerUrl": "http://localhost:8086", "Bucket": "b" }""", "Org" )]
    [TestCase( """{ "ServerUrl": "http://localhost:8086", "Org": "o" }""", "Bucket" )]
    [TestCase( """{ "ServerUrl": "http://localhost:8086", "Org": "o", "Bucket": "b", "Nope": 1 }""", "Nope" )]
    public void an_invalid_configuration_is_an_error( string json, string expected )
    {
        var collector = Collector();
        Assert.That( Read( json, collector ), Is.Null );
        Assert.That( collector.UserMessages.Any( m => m.Level == UserMessageLevel.Error && m.Text.Contains( expected ) ), Is.True );
    }

    [Test]
    public void the_token_and_the_password_are_secrets()
    {
        var d = ConfigurationModelDescription.Get( typeof( InfluxDbConfiguration ) );
        Assert.That( d.Properties.Where( p => p.IsSecret ).Select( p => p.Name ), Is.EquivalentTo( new[] { "Token", "Password" } ) );
    }
}
