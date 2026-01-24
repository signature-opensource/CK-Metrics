using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb.Tests;

/// <summary>
/// A mock InfluxDB server for testing that validates incoming requests.
/// </summary>
public sealed class MockInfluxDbServer : IDisposable
{
    readonly HttpListener _listener;
    readonly CancellationTokenSource _cts;
    readonly Task _serverTask;
    readonly List<ReceivedRequest> _receivedRequests = new();
    readonly object _lock = new();

    HttpStatusCode _responseStatusCode = HttpStatusCode.NoContent;
    string _responseBody = string.Empty;

    /// <summary>
    /// Initializes a new mock InfluxDB server on an available port.
    /// </summary>
    public MockInfluxDbServer()
    {
        // Find an available port
        var port = GetAvailablePort();
        Url = $"http://localhost:{port}";
        Org = "test-org";
        Bucket = "test-bucket";
        Token = "test-token";

        _listener = new HttpListener();
        _listener.Prefixes.Add( $"{Url}/" );
        _listener.Start();

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run( ServerLoopAsync );
    }

    /// <summary>
    /// Gets the base URL of the mock server.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Gets the test organization name.
    /// </summary>
    public string Org { get; }

    /// <summary>
    /// Gets the test bucket name.
    /// </summary>
    public string Bucket { get; }

    /// <summary>
    /// Gets the test API token.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Gets the list of received requests.
    /// </summary>
    public IReadOnlyList<ReceivedRequest> ReceivedRequests
    {
        get
        {
            lock( _lock )
            {
                return _receivedRequests.ToList();
            }
        }
    }

    /// <summary>
    /// Sets the response status code for subsequent requests.
    /// </summary>
    public void SetResponseStatus( HttpStatusCode statusCode, string body = "" )
    {
        _responseStatusCode = statusCode;
        _responseBody = body;
    }

    /// <summary>
    /// Clears the list of received requests.
    /// </summary>
    public void ClearReceivedRequests()
    {
        lock( _lock )
        {
            _receivedRequests.Clear();
        }
    }

    /// <summary>
    /// Asserts that the server received at least one valid write request.
    /// </summary>
    public void AssertReceivedValidLineProtocol()
    {
        lock( _lock )
        {
            if( _receivedRequests.Count == 0 )
                throw new InvalidOperationException( "No requests were received." );

            foreach( var request in _receivedRequests )
            {
                if( request.Path.StartsWith( "/api/v2/write" ) )
                {
                    if( string.IsNullOrWhiteSpace( request.Body ) )
                        throw new InvalidOperationException( "Request body is empty." );

                    // Verify basic line protocol format (at least one line with measurement and value)
                    var lines = request.Body.Split( '\n', StringSplitOptions.RemoveEmptyEntries );
                    if( lines.Length == 0 )
                        throw new InvalidOperationException( "No line protocol lines found in request body." );

                    foreach( var line in lines )
                    {
                        if( !line.Contains( " value=" ) )
                            throw new InvalidOperationException( $"Line protocol line missing 'value=' field: {line}" );
                    }

                    return;
                }
            }

            throw new InvalidOperationException( "No write requests were received." );
        }
    }

    async Task ServerLoopAsync()
    {
        while( !_cts.IsCancellationRequested )
        {
            try
            {
                // Don't use Task.WhenAny with GetContextAsync - it causes race conditions
                // where requests get stranded when timeout wins. Instead, just await directly.
                var context = await _listener.GetContextAsync().WaitAsync( _cts.Token );
                _ = HandleRequestAsync( context ); // Fire and forget to not block the loop
            }
            catch( OperationCanceledException )
            {
                break;
            }
            catch( HttpListenerException )
            {
                // HttpListener was stopped
                break;
            }
            catch( TimeoutException )
            {
                // Continue loop
                if( _cts.IsCancellationRequested )
                    break;
            }
            catch
            {
                // Ignore other errors, continue loop
                if( _cts.IsCancellationRequested )
                    break;
            }
        }
    }

    async Task HandleRequestAsync( HttpListenerContext context )
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Read body
            string body;
            if( request.ContentEncoding != null &&
                request.Headers["Content-Encoding"]?.Contains( "gzip" ) == true )
            {
                using var gzipStream = new GZipStream( request.InputStream, CompressionMode.Decompress );
                using var reader = new StreamReader( gzipStream, Encoding.UTF8 );
                body = await reader.ReadToEndAsync();
            }
            else
            {
                using var reader = new StreamReader( request.InputStream, Encoding.UTF8 );
                body = await reader.ReadToEndAsync();
            }

            // Record the request
            var receivedRequest = new ReceivedRequest
            {
                Method = request.HttpMethod,
                Path = request.Url?.PathAndQuery ?? string.Empty,
                Headers = request.Headers.AllKeys
                    .Where( k => k != null )
                    .ToDictionary( k => k!, k => request.Headers[k] ?? string.Empty ),
                Body = body,
                ContentEncoding = request.Headers["Content-Encoding"] ?? string.Empty
            };

            lock( _lock )
            {
                _receivedRequests.Add( receivedRequest );
            }

            // Send response
            response.StatusCode = (int)_responseStatusCode;
            if( !string.IsNullOrEmpty( _responseBody ) )
            {
                var responseBytes = Encoding.UTF8.GetBytes( _responseBody );
                response.ContentLength64 = responseBytes.Length;
                await response.OutputStream.WriteAsync( responseBytes );
            }
        }
        finally
        {
            response.Close();
        }
    }

    static int GetAvailablePort()
    {
        var listener = new TcpListener( IPAddress.Loopback, 0 );
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
            // Synchronous wait is acceptable here as Dispose must be synchronous
            // and we need to ensure the server task completes before disposing resources.
#pragma warning disable VSTHRD002
            _serverTask.Wait( TimeSpan.FromSeconds( 5 ) );
#pragma warning restore VSTHRD002
        }
        catch
        {
            // Ignore errors during shutdown
        }
        _cts.Dispose();
    }

    /// <summary>
    /// Represents a received HTTP request.
    /// </summary>
    public sealed class ReceivedRequest
    {
        public string Method { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public Dictionary<string, string> Headers { get; init; } = new();
        public string Body { get; init; } = string.Empty;
        public string ContentEncoding { get; init; } = string.Empty;
    }
}
