using System.Net;
using System.Text;

namespace TypeSafe.AI.Tests;

/// <summary>Records every attempt (uri, body bytes, auth, content type) and serves queued responses.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<(Uri? Uri, byte[] Body, string? Authorization, string? ContentType)> Requests { get; } = [];
    public List<HttpRequestMessage> SentMessages { get; } = [];
    public List<DateTimeOffset> AttemptTimes { get; } = [];

    public void Enqueue(HttpStatusCode statusCode, string? json = null, Action<HttpResponseMessage>? configure = null) =>
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(statusCode);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    /// <summary>Delays before responding so the per-attempt Polly timeout fires during the send phase.</summary>
    public void EnqueueStalledHeaders(int stallMilliseconds) =>
        _responses.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(stallMilliseconds, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

    public void EnqueueThrow(Exception exception) =>
        _responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public void EnqueueStalledBody() =>
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
            return Task.FromResult(response);
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AttemptTimes.Add(DateTimeOffset.UtcNow);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add((request.RequestUri, body,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.ToString()));
        SentMessages.Add(request);
        return await _responses.Dequeue()(request, cancellationToken);
    }
}

/// <summary>A stream that never yields bytes; ReadAsync only completes through cancellation.</summary>
internal sealed class StalledStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}
