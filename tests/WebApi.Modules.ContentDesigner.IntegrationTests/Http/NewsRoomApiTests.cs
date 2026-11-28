using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.IntegrationTests.Http;

/// <summary>
/// How the module asks NewsRoom about a post, over HTTP answers: whether NewsRoom is there, who may be believed when
/// it lists nothing, and whose token each call carries.
/// </summary>
public class NewsRoomApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Nothing = """{"items":[],"page":1,"pageSize":20,"total":0}""";
    private const string SeesAll = """{"userId":"u","permissions":[{"permission":"newsroom.projects","tenantWide":true,"unitIds":[]}]}""";
    private const string SeesAUnit =
        """{"userId":"u","permissions":[{"permission":"newsroom.moderate","tenantWide":false,"unitIds":["0190b5a1-0000-7000-8000-000000000001"]},{"permission":"newsroom.manage","tenantWide":true,"unitIds":[]}]}""";

    /// <summary>The gateway and the identity service. <c>answer</c> gets the path and whether the service asks.</summary>
    private sealed class Platform(Func<string, bool, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Calls { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.EndsWith("/connect/token", StringComparison.Ordinal))
                return Reply(HttpStatusCode.OK, """{"access_token":"service-token","expires_in":300}""");
            var service = request.Headers.Authorization?.Parameter == "service-token";
            Calls.Add($"{(service ? "service" : "caller")} {path} {string.Join(',', request.Headers.GetValues(TenancyNames.Header))}");
            var (status, body) = answer(path, service);
            return Reply(status, body);
        }

        private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }

    private static NewsRoomApi Api(Platform platform, bool serviceClient)
    {
        var options = Options.Create(new ContentDesignerOptions
        {
            ApiBaseUrl = "http://gateway",
            ServiceAuthority = serviceClient ? "http://identity" : "",
            ServiceClientId = serviceClient ? "designer" : "",
            ServiceClientSecret = serviceClient ? "test-only" : ""
        });
        var request = new DefaultHttpContext();
        request.Request.Headers.Authorization = "Bearer caller-token";
        request.Request.Headers[TenancyNames.Header] = "acme";
        var gateway = new Gateway(platform.CreateClient("gateway"), new HttpContextAccessor { HttpContext = request },
            Substitute.For<ITenantContext>(), new ServiceTokenSource(platform, options), options);
        return new NewsRoomApi(gateway, NullLogger<NewsRoomApi>.Instance);
    }

    private static string Listing(Guid id, string state) => $$"""{"items":[{"id":"{{id}}","state":"{{state}}","title":"T"}],"total":1}""";

    [Fact]
    public async Task TheServiceClientAsksInTheCallersTenant()
    {
        var id = Guid.NewGuid();
        var platform = new Platform((_, _) => (HttpStatusCode.OK, Listing(id, "InProduction")));

        var found = await Api(platform, serviceClient: true).GetPostProjectsAsync(7, Ct);

        found.Answer.ShouldBe(NewsRoomAnswer.Known);
        found.Projects.ShouldBe([new NewsRoomProject(id, "InProduction")]);
        platform.Calls.ShouldBe(["service /api/newsroom/projects?postId=7&pageSize=20 acme"]);
    }

    [Fact]
    public async Task A404MeansNewsRoomIsNotThere() =>
        (await Api(new Platform((_, _) => (HttpStatusCode.NotFound, "")), serviceClient: false).GetPostProjectsAsync(7, Ct))
            .Answer.ShouldBe(NewsRoomAnswer.NotInstalled);

    [Fact]
    public async Task NothingListedCountsFromSomeoneWhoSeesEveryProject()
    {
        var platform = new Platform((path, _) => (HttpStatusCode.OK, path.EndsWith("/access/mine", StringComparison.Ordinal) ? SeesAll : Nothing));

        var found = await Api(platform, serviceClient: true).GetPostProjectsAsync(7, Ct);

        found.Answer.ShouldBe(NewsRoomAnswer.Known);
        found.Projects.ShouldBeEmpty();
    }

    [Fact]
    public async Task NothingListedProvesNothingFromSomeoneWhoSeesOneUnit()
    {
        var platform = new Platform((path, _) => (HttpStatusCode.OK, path.EndsWith("/access/mine", StringComparison.Ordinal) ? SeesAUnit : Nothing));

        (await Api(platform, serviceClient: false).GetPostProjectsAsync(7, Ct)).Answer.ShouldBe(NewsRoomAnswer.CannotSee);
    }

    [Fact]
    public async Task AServiceClientWithoutAGrantLeavesTheCallerToAnswer()
    {
        var id = Guid.NewGuid();
        var platform = new Platform((_, service) => service ? (HttpStatusCode.Forbidden, "") : (HttpStatusCode.OK, Listing(id, "Killed")));

        var found = await Api(platform, serviceClient: true).GetPostProjectsAsync(7, Ct);

        found.Projects.ShouldBe([new NewsRoomProject(id, "Killed")]);
        platform.Calls.Count.ShouldBe(2);
        platform.Calls[1].ShouldStartWith("caller ");
    }

    [Fact]
    public async Task ACallerNewsRoomRefusesCannotSee() =>
        (await Api(new Platform((_, _) => (HttpStatusCode.Forbidden, "")), serviceClient: false).GetPostProjectsAsync(7, Ct))
            .Answer.ShouldBe(NewsRoomAnswer.CannotSee);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "<html></html>")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task AFailureOrAnUnreadableAnswerIsUnreachable(HttpStatusCode status, string body) =>
        (await Api(new Platform((_, _) => (status, body)), serviceClient: false).GetPostProjectsAsync(7, Ct))
            .Answer.ShouldBe(NewsRoomAnswer.Unreachable);

    [Fact]
    public async Task NoConnectionIsUnreachable() =>
        (await Api(new Platform((_, _) => throw new HttpRequestException("refused")), serviceClient: false).GetPostProjectsAsync(7, Ct))
            .Answer.ShouldBe(NewsRoomAnswer.Unreachable);
}
