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
//  REMOTE EXECUTION DIVISION — MODULE A8
//  "Function Apps: Remote Calls, Route Selection, And A Healthy Respect For HTTP"
//
//  Up to this point, Function Apps appeared as supporting cast.
//  Useful. Necessary. A little mysterious.
//  This module fixes that.
//
//  A8 demonstrates the three questions every consumer eventually asks:
//    1. Can the framework reach my Function App at all?
//    2. Can it infer the route from the function method metadata?
//    3. Can it still behave when I want to shape the HTTP request myself?
//
//  The answer to all three is yes. Miracles do happen.
//  We just prefer ones backed by assertions.
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
    // First lesson: let the framework read the route metadata from the function.
    // Less guesswork. Fewer strings. Lower odds of inventing your own endpoint by accident.

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
    // Second lesson: when you need to control body and headers, do it in the timeline.
    // Keep the distributed choreography visible. Hidden choreography becomes folklore.

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
    // Third lesson: if the route is the default api/{functionName} pattern, selecting by function name is enough.
    // Clean. Direct. Almost suspiciously cooperative.

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