using System.Net;
using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class ReleaseHealthSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "patchlight-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ReleaseHealthSource Source(HttpMessageHandler handler) => new(new HttpClient(handler), _dir, _time);

    [Fact]
    public void Builds_the_page_address_from_the_version() =>
        Assert.Equal(ReleaseHealthParserTests.Url, ReleaseHealthSource.PageUrl("25H2"));

    [Fact]
    public async Task Downloads_and_parses_the_page()
    {
        var handler = new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()));

        var snapshot = await Source(handler).GetAsync("25H2", CancellationToken.None);

        Assert.Equal(11, snapshot.Issues.Count);
        Assert.False(snapshot.FromCache);
        Assert.Equal(_time.GetUtcNow(), snapshot.FetchedAt);
        Assert.Equal(ReleaseHealthParserTests.Url, handler.LastUri?.ToString());
    }

    [Fact]
    public async Task Uses_the_saved_copy_when_offline()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);
        var savedAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromDays(2));

        var snapshot = await Source(new StubHandler(_ => throw new HttpRequestException("offline")))
            .GetAsync("25H2", CancellationToken.None);

        Assert.True(snapshot.FromCache);
        Assert.Equal(11, snapshot.Issues.Count);
        Assert.Equal(savedAt, snapshot.FetchedAt);
    }

    [Fact]
    public async Task Changed_page_does_not_overwrite_the_saved_copy()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);

        var snapshot = await Source(new StubHandler(_ => Ok("<html><body>moved</body></html>")))
            .GetAsync("25H2", CancellationToken.None);

        Assert.True(snapshot.FromCache);
        Assert.Equal(11, snapshot.Issues.Count);
    }

    [Fact]
    public async Task Offline_with_no_saved_copy_is_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            Source(new StubHandler(_ => throw new HttpRequestException("offline"))).GetAsync("25H2", CancellationToken.None));
        Assert.Contains("Couldn't reach Microsoft", ex.Message);
    }

    [Fact]
    public async Task Server_error_falls_back_like_offline()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);

        var snapshot = await Source(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
            .GetAsync("25H2", CancellationToken.None);

        Assert.True(snapshot.FromCache);
        Assert.Equal(11, snapshot.Issues.Count);
    }

    [Fact]
    public async Task Server_error_with_no_saved_copy_is_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            Source(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
                .GetAsync("25H2", CancellationToken.None));
        Assert.Contains("Couldn't reach Microsoft", ex.Message);
    }

    [Fact]
    public async Task Timeout_falls_back_to_the_saved_copy()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);

        var snapshot = await Source(new StubHandler(_ => throw new TaskCanceledException("timed out")))
            .GetAsync("25H2", CancellationToken.None);

        Assert.True(snapshot.FromCache);
        Assert.Equal(11, snapshot.Issues.Count);
    }

    [Fact]
    public async Task Cancelling_does_not_fall_back_to_the_saved_copy()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", cts.Token));
    }

    [Fact]
    public async Task Unwritable_cache_still_returns_the_fresh_download()
    {
        // A directory where the cache file should go makes the save fail.
        Directory.CreateDirectory(Path.Combine(_dir, "release-health-25h2.html"));

        var snapshot = await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml())))
            .GetAsync("25H2", CancellationToken.None);

        Assert.Equal(11, snapshot.Issues.Count);
        Assert.False(snapshot.FromCache);
    }

    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }
}
