using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Search.Commands.EmbedTasks;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Search;

/// <summary>
/// POST /api/search/tasks (SPEC section 19.6) on the real API: chat.use from the token, the question in the body,
/// the nearest tasks first; 409 Search.TurnedOff on the API with the vectorization off, 503 Search.ModelFailed when
/// the model doesn't answer, 400 on question without one. The question stays out of the API's logs. The filters'
/// rules are in SearchTasksTests, on the handler.
/// </summary>
public sealed class SearchEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private const string Search = "/api/search/tasks";
    private const string Question = "raportul pe luna asta";

    private HttpClient _searching = null!;

    private FakeEmbeddingModel Model => Api.EmbeddingModel;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Api.EmbeddingModel = new FakeEmbeddingModel().Give(Question, 1f, 0f);
        _searching = Api.Searching.CreateClient();
    }

    public override async Task DisposeAsync()
    {
        _searching.Dispose();
        await base.DisposeAsync();
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Search) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    /// <summary>Two tasks made through the API, then their vectors, as the worker would make them.</summary>
    private async Task<string> TwoTasks()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("CAU").OwnedBy(owner));
        foreach (var (title, vector) in new[] { ("Imprimanta de la etajul 2", new[] { 0f, 1f }), ("Raport lunar pentru client", new[] { 1f, 0f }) })
        {
            Model.Give(title, vector);
            var created = await Send(HttpMethod.Post, $"{Projects}/{project.Id}/tasks", token, new { title, priority = "Medium" });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await using var scope = Api.Searching.Services.CreateAsyncScope();
        var embedded = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EmbedTasksCommand, EmbeddedTasks>>()
            .Handle(new EmbedTasksCommand(64), CancellationToken.None);
        embedded.IsSuccess.Should().BeTrue();
        return project.Key;
    }

    [Fact]
    public async Task Search_WithChatUse_AnswersTheNearestTasksFirst_WithTheirDistance()
    {
        var key = await TwoTasks();
        var token = await TokenWith(Permissions.ChatUse);

        var response = await Post(_searching, token, new { question = Question });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var tasks = json.RootElement.GetProperty("tasks").EnumerateArray().ToList();
        tasks.Select(task => task.GetProperty("title").GetString())
            .Should().Equal("Raport lunar pentru client", "Imprimanta de la etajul 2");
        tasks[0].GetProperty("key").GetString().Should().Be($"{key}-2");
        tasks[0].GetProperty("distance").GetDouble().Should().BeApproximately(0, 1e-6);
        tasks[1].GetProperty("distance").GetDouble().Should().BeApproximately(1, 1e-6);
        tasks[0].GetProperty("archived").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("tokens").GetInt32().Should().Be(FakeEmbeddingModel.TokensOf(Question));
    }

    [Fact]
    public async Task Search_WithoutChatUse_Returns403_AndCallsNoModel()
    {
        var token = await TokenWith([.. Permissions.All.Where(permission => permission != Permissions.ChatUse)]);

        var response = await Post(_searching, token, new { question = Question });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
        Model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_OnTheApiWithTheVectorizationOff_Returns409SearchTurnedOff()
    {
        var token = await TokenWith(Permissions.ChatUse);

        var response = await Post(Client, token, new { question = Question });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Search.TurnedOff");
    }

    [Fact]
    public async Task Search_WhenTheModelDoesntAnswer_Returns503SearchModelFailed()
    {
        var token = await TokenWith(Permissions.ChatUse);
        Model.Fail();

        var response = await Post(_searching, token, new { question = Question });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeFrom(response)).Should().Be("Search.ModelFailed");
    }

    [Fact]
    public async Task Search_WithoutAQuestion_Returns400OnQuestion()
    {
        var token = await TokenWith(Permissions.ChatUse);

        var response = await Post(_searching, token, new { limit = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").TryGetProperty("question", out _).Should().BeTrue();
        Model.Calls.Should().BeEmpty();
    }

    // SPEC section 19.5: the logs never hold a text sent to the model, and the question goes in the body for that.
    [Fact]
    public async Task Search_KeepsTheQuestionOutOfTheApisLogs()
    {
        await TwoTasks();
        var token = await TokenWith(Permissions.ChatUse);

        (await Post(_searching, token, new { question = Question })).StatusCode.Should().Be(HttpStatusCode.OK);

        Api.Searching.Logs.Events.Should().Contain(entry => entry.RenderMessage(null).Contains("SearchTasksCommand", StringComparison.Ordinal));
        Api.Searching.Logs.Events.Should().NotContain(entry =>
            entry.RenderMessage(null).Contains(Question, StringComparison.OrdinalIgnoreCase)
            || entry.Properties.Values.Any(value => value.ToString().Contains(Question, StringComparison.OrdinalIgnoreCase)));
    }
}
