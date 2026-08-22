using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using FunctionApp;
using TestFramework.Azure;
using TestFramework.Azure.FunctionApp.Results;
using TestFramework.Azure.Identifier;
using TestFramework.Azure.Runtime;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;
namespace TestFramework.Showroom.Azure;

//doc: Can we hit the Function App, or are we just being optimistic?
//doc:
//doc: Up to now the Function App has mostly acted as a useful accomplice in larger scenarios. That
//doc: arrangement is over. This chapter drags it into the centre of the room and asks the questions people
//doc: actually care about, loudly and with intent - and they are operational rather than philosophical:
//doc:
//doc: 1. Can the framework reach the app at all?
//doc: 2. Can a route be discovered from method metadata instead of hand-typed hope?
//doc: 3. Can you still shape the HTTP request yourself when you want full control?
//doc:
//doc: If those three answers are not solid, the rest of the integration story is just decorative wiring with
//doc: a motivational budget.
//doc:
//doc: Three ways to select an endpoint appear below, in decreasing order of cleverness:
//doc: `SelectEndpointWithMethod<T>(nameof(T.Method))` reads the route off the function method,
//doc: `SelectFunction("Name", method)` uses the default `api/{functionName}` convention, and between them
//doc: sits explicit request shaping with headers and a body. Prefer the first: the fewer magic strings you
//doc: hand-maintain, the fewer chances you have to confidently call the wrong thing and defend it in chat.

//doc: The definitions come first, and they are the same shape as everywhere else - one app, its storage, its
//doc: Cosmos container, its bus, and the emulator topology those names require. Note the app is declared
//doc: separately from the ones in `AzureShowroom.cs` even though the resources overlap: this chapter uses
//doc: `DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>()` directly, so that it owns exactly what it
//doc: needs and nothing more.

internal sealed class ShowroomFunctionAppDefinition : DockerFunctionAppDefinition<HttpTests>
{
    public override FunctionAppIdentifier Identifier => "ShowroomFunction";

    protected override void Configure(DockerFunctionAppBuilder builder)
    {
        builder
            .UseStorage<ShowroomStorageDefinition>(tableNameSettingName: "StorageTableName")
            .UseCosmos<ShowroomCosmosDefinition>()
                .UseServiceBusTrigger<ShowroomBusDefinition>(d => d.Submission)
                .UseServiceBusReply<ShowroomBusDefinition>(d => d.Reply);
    }
}

internal sealed class ShowroomStorageDefinition : DockerStorageDefinition
{
    public override StorageAccountIdentifier Identifier => "MainStorage";

    protected override string? BlobContainerName => "showroom-blob";
    protected override string? QueueContainerName => "showroom-queue";
    protected override string? TableContainerName => "MainTable";
}

internal sealed class ShowroomCosmosDefinition : DockerCosmosDefinition<CandidateProfile>
{
    public override CosmosContainerIdentifier Identifier => "MainDb";

    protected override string? DatabaseName => "BaseDB";
    protected override string? ContainerName => "BaseContainer";
}
internal sealed class ShowroomBusDefinition : DockerServiceBusDefinition
{
    public override ServiceBusIdentifier Identifier => "ShowroomBus";

    public DockerServiceBusEndpoint Submission
        => DockerServiceBusEndpoint.TopicSubscription("sbt-int-in", "Default");

    public DockerServiceBusEndpoint Reply
        => DockerServiceBusEndpoint.TopicSubscription("sbt-int-out", "Default");

    protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
        => ShowroomServiceBusTopology.Configure(builder);
}

internal static class ShowroomServiceBusTopology
{
    internal static void Configure(DockerServiceBusTopologyBuilder builder)
    {
        builder.AddNamespace("sbemulatorns", ns => ns
            .AddTopic("sbt-int-in", topic => topic.AddSubscription("Default"))
            .AddTopic("sbt-int-out", topic => topic.AddSubscription("Default")));
    }
}

//doc: First move: reach it, then call it. The liveness probe answers question one on its own -
//doc: `AlivenessLevel.Reachable` proves the socket opened, without any claim about what is behind it - and
//doc: then the route is discovered from the function method's own metadata.
//doc:
//doc: Two details worth carrying forward. Both steps override the default timeout with one minute, because a
//doc: container that has to start is a different kind of wait than an HTTP call to something already running.
//doc: And the response arrives as a `HttpResponseResultContext` on the named step's `LastResult` - a status
//doc: code and a body, asserted like data, exactly as in the web lane's chapter W1.

public class FunctionApps_RouteDiscovery(ITestOutputHelper outputHelper)
{

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureExt.Trigger.IsLive.FunctionApp("ShowroomFunction", AlivenessLevel.Reachable))
            .WithTimeOut(TimeSpan.FromMinutes(1))
            .Name("function-live")
        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("ShowroomFunction")
                .SelectEndpointWithMethod<HttpTests>(nameof(HttpTests.Run))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(1))
            .Name("function-call")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = config.BuildServiceProvider();

        TimelineRun run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        HttpResponseResultContext response = Assert.IsType<HttpResponseResultContext>(run.Step("function-call").LastResult.Result);
        string body = Assert.IsType<string>(response.Body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The HTTP trigger function executed successfully.", body, StringComparison.Ordinal);
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.FunctionAppComponentId));
    }
}

//doc: Second move: when the headers and the body matter, shape them in the timeline. `WithHeader` and
//doc: `WithBody` take variable references like everything else, so the request is data rather than a helper
//doc: method with an innocent name hiding three decisions.
//doc:
//doc: The echo function reflects what it received, which is why the assertions can check method, header and
//doc: body in one go. Distributed behaviour should stay visible where the test can interrogate it.

public class FunctionApps_ExplicitHttpShaping(ITestOutputHelper outputHelper)
{

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("ShowroomFunction")
                .SelectEndpointWithMethod<HttpTests>(nameof(HttpTests.Echo))
                .WithHeader(Var.Const("x-test"), Var.Const("showroom"))
                .WithBody(Var.Const("payload=calibrated"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(1))
            .Name("function-echo")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = config.BuildServiceProvider();

        TimelineRun run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        HttpResponseResultContext response = Assert.IsType<HttpResponseResultContext>(run.Step("function-echo").LastResult.Result);
        string body = Assert.IsType<string>(response.Body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Method=POST", body, StringComparison.Ordinal);
        Assert.Contains("XTest=showroom", body, StringComparison.Ordinal);
        Assert.Contains("Body=payload=calibrated", body, StringComparison.Ordinal);
    }
}

//doc: Third move, and the simplest: if the app keeps the default `api/{functionName}` route, selecting by
//doc: function name is enough. No scavenger hunt, no custom map. Use the convention and cash the simplicity
//doc: before somebody "improves" it.
//doc:
//doc: Note that the method has to be stated here - `SelectFunction("HttpEchoTest", HttpMethod.Post)` - where
//doc: the metadata-driven overload knew it already. That is the trade: one less type reference, one more
//doc: thing you are asserting by hand.

public class FunctionApps_DefaultFunctionRoute(ITestOutputHelper outputHelper)
{

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("ShowroomFunction")
                .SelectFunction("HttpEchoTest", HttpMethod.Post)
                .WithBody(Var.Const("payload=default-route"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(1))
            .Name("function-default-route")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = config.BuildServiceProvider();

        TimelineRun run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        HttpResponseResultContext response = Assert.IsType<HttpResponseResultContext>(run.Step("function-default-route").LastResult.Result);
        string body = Assert.IsType<string>(response.Body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Method=POST", body, StringComparison.Ordinal);
        Assert.Contains("Body=payload=default-route", body, StringComparison.Ordinal);
    }
}