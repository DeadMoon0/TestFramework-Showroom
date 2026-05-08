using System.Net;
using System.Text.Json;
using TestFramework.Azure;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Azure.Identifier;
using TestFramework.Azure.LogicApp;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  WORKFLOW SUPERVISION DIVISION — MODULE A9
//  "Logic Apps: Stateful Runs, Stateless Results, And The Difference Between Them"
//
//  This is where Logic Apps stop being "that other Azure thing" and start behaving
//  like a test surface with rules.
//
//  Those rules matter:
//    1. Stateful workflows can be observed as durable runs.
//    2. Stateless workflows finish inline and should be asserted inline.
//    3. Timer workflows are not HTTP callbacks wearing a fake mustache.
//       They have their own trigger shape and deserve to be treated accordingly.
//
//  Respect the model and the framework stays readable.
//  Ignore the model and the error messages become educational.
// ══════════════════════════════════════════════════════════════════════════════

internal sealed class ShowroomLogicAppDefinition : DockerLogicAppDefinition
{
    private static readonly string ShowroomLogicAppRootPath = System.IO.Path.Combine("TestFramework-Showroom", "Azure", "LogicApp");

    public override LogicAppIdentifier Identifier => "ShowroomLogic";

    public override string Path => ShowroomLogicAppRootPath;

    protected override LogicAppConfig? CreateDefaultConfig() => new()
    {
        WorkflowName = "ShowroomStatefulWorkflow",
        Standard = new LogicAppStandardConfig
        {
            BaseUrl = "http://localhost/",
        },
    };
}

[Collection("AzureShowroom")]
public class LogicApps_StatefulRunTracking(ITestOutputHelper outputHelper)
{
    // Stateful workflow: call it, keep the run context, then wait for completion.
    // That sounds like more work because it is more work.
    // Durable orchestration charges interest in bookkeeping.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureTF.Trigger.LogicApp
                .Http("ShowroomLogic")
                .Workflow("ShowroomStatefulWorkflow")
                .Manual()
                .WithBody(Var.Const("{\"batch\":\"stateful\"}"))
                .CallForRunContext())
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-call")
        .CaptureResultAs<LogicAppRunContext>("logicRun")
        .WaitForEvent(AzureTF.Event.LogicApp.RunCompleted("ShowroomLogic", Var.Ref<LogicAppRunContext>("logicRun")))
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-completed")
        .Build();

    [Fact]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        TimelineRun run = await _timeline
            .SetupRun(config.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomLogicAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        LogicAppRunContext started = Assert.IsType<LogicAppRunContext>(run.Step("logic-call").LastResult.Result);
        LogicAppRunDetails completed = Assert.IsType<LogicAppRunDetails>(run.Step("logic-completed").LastResult.Result);

        Assert.Equal("ShowroomStatefulWorkflow", started.WorkflowName);
        Assert.False(string.IsNullOrWhiteSpace(started.RunId));
        Assert.Equal(started.RunId, completed.RunId);
        Assert.Equal(LogicAppRunStatus.Succeeded, completed.Status);
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.LogicAppComponentId));
    }
}

[Collection("AzureShowroom")]
public class LogicApps_StatelessCapture(ITestOutputHelper outputHelper)
{
    // Stateless workflow: no durable run history contract, no ceremonial polling.
    // Trigger it, capture the inline result, inspect what came back, move on with your life.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureTF.Trigger.LogicApp
                .Http("ShowroomLogic")
                .Workflow("ShowroomStatelessWorkflow")
                .Manual()
                .WithBody(Var.Const("{\"batch\":\"stateless\"}"))
                .CallAndCapture())
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-stateless")
        .Build();

    [Fact]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        TimelineRun run = await _timeline
            .SetupRun(config.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomLogicAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        LogicAppCapturedResult result = Assert.IsType<LogicAppCapturedResult>(run.Step("logic-stateless").LastResult.Result);
        using JsonDocument payload = JsonDocument.Parse(result.ResponseBody);
        using JsonDocument receivedPayload = JsonDocument.Parse(payload.RootElement.GetProperty("received").GetString()!);

        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        Assert.Equal(LogicAppRunStatus.Succeeded, result.Status);
        Assert.Equal("ShowroomStatelessWorkflow", result.WorkflowName);
        Assert.Equal("manual", result.TriggerName);
        Assert.Equal("showroom-stateless-processed", payload.RootElement.GetProperty("message").GetString());
        Assert.Equal("stateless", receivedPayload.RootElement.GetProperty("batch").GetString());
    }
}

[Collection("AzureShowroom")]
public class LogicApps_TimerWorkflow(ITestOutputHelper outputHelper)
{
    // Timer workflow: no HTTP body, no callback payload, just a management-triggered run.
    // The useful output is the finished run, not the request body you never had.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureTF.Trigger.LogicApp
                .Http("ShowroomLogic")
                .Workflow("ShowroomTimerWorkflow")
                .Timer()
                .CallForRunContext())
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-timer-call")
        .CaptureResultAs<LogicAppRunContext>("logicTimerRun")
        .WaitForEvent(AzureTF.Event.LogicApp.RunCompleted("ShowroomLogic", Var.Ref<LogicAppRunContext>("logicTimerRun")))
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-timer-completed")
        .Build();

    [Fact]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        TimelineRun run = await _timeline
            .SetupRun(config.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<ShowroomLogicAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        LogicAppRunContext started = Assert.IsType<LogicAppRunContext>(run.Step("logic-timer-call").LastResult.Result);
        LogicAppRunDetails completed = Assert.IsType<LogicAppRunDetails>(run.Step("logic-timer-completed").LastResult.Result);

        Assert.Equal("ShowroomTimerWorkflow", started.WorkflowName);
        Assert.False(string.IsNullOrWhiteSpace(started.RunId));
        Assert.Equal(started.RunId, completed.RunId);
        Assert.Equal(LogicAppRunStatus.Succeeded, completed.Status);
    }
}