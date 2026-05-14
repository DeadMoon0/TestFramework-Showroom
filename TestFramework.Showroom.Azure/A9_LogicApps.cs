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
//  WORKFLOW SUPERVISION DIVISION - MODULE A9
//  "Three Workflow Shapes. Three Different Jobs. Do Not Confuse Them."
//
//  Logic Apps have a talent for looking similar right up until you assert them
//  the wrong way. Then they become extremely interested in teaching you the
//  difference between stateful, stateless, and timer-driven behavior in a tone nobody enjoys.
//
//  So we will learn the cheap way instead:
//    1. Stateful workflows produce durable runs you can track and wait on.
//    2. Stateless workflows finish inline and should be inspected inline.
//    3. Timer workflows are management-triggered runs, not fake manual calls.
//
//  Treat each shape like the thing it actually is, and the test model stays
//  clean enough to trust and smug enough to survive code review.
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

public class LogicApps_StatefulRunTracking(ITestOutputHelper outputHelper)
{
    // Stateful workflow: trigger it, keep the run handle, then wait for the
    // durable run to finish. Yes, it is more bookkeeping. That is the price of
    // asking the platform to remember what happened after your HTTP request left the building.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureExt.Trigger.LogicApp
                .Http("ShowroomLogic")
                .Workflow("ShowroomStatefulWorkflow")
                .Manual()
                .WithBody(Var.Const("{\"batch\":\"stateful\"}"))
                .CallForRunContext())
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-call")
        .GetRunContext("logicRun")
        .WaitForEvent(AzureExt.Event.LogicApp.RunCompleted("ShowroomLogic", Var.Ref<LogicAppRunContext>("logicRun")))
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

public class LogicApps_StatelessCapture(ITestOutputHelper outputHelper)
{
    // Stateless workflow: no durable run history, no reason to pretend there is
    // one. Fire it, capture the inline result, inspect it, and keep moving before ceremony starts breeding.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureExt.Trigger.LogicApp
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

public class LogicApps_TimerWorkflow(ITestOutputHelper outputHelper)
{
    // Timer workflow: no request body, no human-supplied payload, just a managed
    // trigger and the finished run details on the other side. Different shape,
    // different assertions, same demand for clarity and zero tolerance for improvisation.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            AzureExt.Trigger.LogicApp
                .Http("ShowroomLogic")
                .Workflow("ShowroomTimerWorkflow")
                .Timer()
                .CallForRunContext())
        .WithTimeOut(TimeSpan.FromMinutes(2))
        .Name("logic-timer-call")
        .GetRunContext("logicTimerRun")
        .WaitForEvent(AzureExt.Event.LogicApp.RunCompleted("ShowroomLogic", Var.Ref<LogicAppRunContext>("logicTimerRun")))
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