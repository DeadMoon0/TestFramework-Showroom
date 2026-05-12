using System.Net;
using System.Net.Http;
using FunctionApp;
using TestFramework.Azure;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Identifier;
using TestFramework.Azure.Runtime;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  REMOTE EXECUTION DIVISION - MODULE A8
//  "Can We Hit The Function App, Or Are We Just Being Optimistic?"
//
//  Up to now the Function App has mostly acted like a useful accomplice in
//  larger scenarios. That arrangement is over. This chapter drags it into the
//  center of the room and asks the questions people actually care about, loudly and with intent.
//
//  Not philosophical questions. Operational questions.
//    1. Can the framework reach the app at all?
//    2. Can route discovery work from method metadata instead of hand-typed hope?
//    3. Can you still shape the HTTP request when you want full control?
//
//  If those three answers are not solid, the rest of the integration story is
//  just decorative wiring with a motivational budget.
// ══════════════════════════════════════════════════════════════════════════════

internal sealed class ShowroomFunctionAppDefinition : DockerFunctionAppDefinition<HttpTests>
{
    private const string LocalFunctionKey = "local-test-key";

    public override FunctionAppIdentifier Identifier => "ShowroomFunction";

    protected override FunctionAppConfig? CreateDefaultConfig() => new()
    {
        BaseUrl = "http://localhost/",
        Code = LocalFunctionKey,
        AdminCode = LocalFunctionKey,
    };
}

[Collection("AzureShowroom")]
public class FunctionApps_RouteDiscovery(ITestOutputHelper outputHelper)
{
    // First move: let the framework discover the route from the function method
    // metadata. The fewer magic strings you hand-maintain, the fewer chances you
    // have to confidently call the wrong thing and defend it in chat.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureTF.Trigger.IsLive.FunctionApp("ShowroomFunction", AlivenessLevel.Reachable)).WithTimeOut(TimeSpan.FromMinutes(1))
        .Name("function-live")
        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("ShowroomFunction")
                .SelectEndpointWithMethod<HttpTests>(nameof(HttpTests.Run))
                .Call())
        .WithTimeOut(TimeSpan.FromMinutes(1))
        .Name("function-call")
        .Build();

    [Fact]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        TimelineRun run = await _timeline
            .SetupRun(config.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        HttpResponseMessage response = Assert.IsType<HttpResponseMessage>(run.Step("function-call").LastResult.Result);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The HTTP trigger function executed successfully.", body, StringComparison.Ordinal);
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.FunctionAppComponentId));
    }
}

[Collection("AzureShowroom")]
public class FunctionApps_ExplicitHttpShaping(ITestOutputHelper outputHelper)
{
    // Second move: when headers and body matter, shape them in the timeline.
    // Distributed behavior should stay visible where the test can interrogate it instead of hiding behind helper methods with innocent names.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("ShowroomFunction")
                .SelectEndpointWithMethod<HttpTests>(nameof(HttpTests.Echo))
                .WithHeader(Var.Const("x-test"), Var.Const("showroom"))
                .WithBody(Var.Const("payload=calibrated"))
                .Call())
        .WithTimeOut(TimeSpan.FromMinutes(1))
        .Name("function-echo")
        .Build();

    [Fact]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        TimelineRun run = await _timeline
            .SetupRun(config.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        HttpResponseMessage response = Assert.IsType<HttpResponseMessage>(run.Step("function-echo").LastResult.Result);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Method=POST", body, StringComparison.Ordinal);
        Assert.Contains("XTest=showroom", body, StringComparison.Ordinal);
        Assert.Contains("Body=payload=calibrated", body, StringComparison.Ordinal);
    }
}

[Collection("AzureShowroom")]
public class FunctionApps_DefaultFunctionRoute(ITestOutputHelper outputHelper)
{
    // Third move: if the app keeps the default api/{functionName} route, selecting
    // by function name is enough. No scavenger hunt. No custom map. Just use the
    // convention and cash the simplicity before somebody "improves" it.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("ShowroomFunction")
                .SelectFunction("HttpEchoTest", HttpMethod.Post)
                .WithBody(Var.Const("payload=default-route"))
                .Call())
        .WithTimeOut(TimeSpan.FromMinutes(1))
        .Name("function-default-route")
        .Build();

    [Fact]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        TimelineRun run = await _timeline
            .SetupRun(config.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        HttpResponseMessage response = Assert.IsType<HttpResponseMessage>(run.Step("function-default-route").LastResult.Result);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Method=POST", body, StringComparison.Ordinal);
        Assert.Contains("Body=payload=default-route", body, StringComparison.Ordinal);
    }
}