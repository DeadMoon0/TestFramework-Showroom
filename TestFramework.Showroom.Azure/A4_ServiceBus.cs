using Azure.Messaging.ServiceBus;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION - PARTICIPANT ORIENTATION MODULE A4
//  "Send A Message. Wait For Reality To Catch Up."
//
//  Service Bus is where the showroom starts caring about time. Not just whether
//  a message can be sent, but whether the right message shows up later and can
//  be identified without guesswork or wishful thinking.
//
//  That is why correlation IDs matter here. Asynchronous systems are already
//  generous with ambiguity. The test does not need to add more and then act surprised.
// ══════════════════════════════════════════════════════════════════════════════

public class ServiceBus_SendAndReceive(ITestOutputHelper outputHelper)
{
    // First example: send one message and wait for the exact correlated receipt.
    // This is the basic asynchronous trust exercise. Very simple. Emotionally difficult.

    private const string CorrelationId = "showroom-42";

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureExt.Trigger.ServiceBus.Send("MainSBTopic", new ServiceBusMessage("Live transmission. Please stand by.") { CorrelationId = CorrelationId }))
        //  ^ Send with a known correlation ID so the wait can demand the exact message later instead of adopting a stranger.
        .WaitForEvent(AzureExt.Event.ServiceBus.MessageReceived("MainSBTopic", correlationId: CorrelationId, completeMessage: true))
            .GetMessage("out")
            // complete = acknowledge and remove the message once observed. Clean hands. Fewer ghosts.
            .WithTimeOut(TimeSpan.FromSeconds(10))
        //  ^ If nothing matching arrives in time, the run fails with a specific timeout and no sympathy.
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        // The received message is exposed as the step output variable. Convenient and slightly smug.
        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist().And().NotBeNull()
            .And().Match(m => m!.CorrelationId == CorrelationId, $"CorrelationId must be '{CorrelationId}'");
        // ^ Correlation closes the loop. Without it, the bus is just plausible noise wearing a badge.
    }
}

public class ServiceBus_QueueSendAndReceive(ITestOutputHelper outputHelper)
{
    private const string CorrelationId = "showroom-queue-42";

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureExt.Trigger.ServiceBus.Send("MainSBQueue", new ServiceBusMessage("Queue delivery. Clean and direct.") { CorrelationId = CorrelationId }))
        .WaitForEvent(AzureExt.Event.ServiceBus.MessageReceived("MainSBQueue", correlationId: CorrelationId, completeMessage: true))
            .GetMessage("out")
            .WithTimeOut(TimeSpan.FromSeconds(10))
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist().And().NotBeNull()
            .And().Match(m => m!.CorrelationId == CorrelationId, $"CorrelationId must be '{CorrelationId}'");
    }
}

public class ServiceBus_SendWithVariable(ITestOutputHelper outputHelper)
{
    // Third example: build the outbound message per run instead of hardcoding it
    // into the static timeline. Same structure, dynamic payload, fewer creative excuses.

    private const string CorrelationId = "showroom-dynamic";
    private const string Subject = "Showroom Test";

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureExt.Trigger.ServiceBus.Send("MainSBTopic", Var.Ref<ServiceBusMessage>("outboundMessage")))
        //    ^ The timeline references a runtime value here because the message is supplied per run, not discovered in a dream.
        .WaitForEvent(AzureExt.Event.ServiceBus.MessageReceived("MainSBTopic", correlationId: CorrelationId, completeMessage: true))
            .GetMessage("out")
            .WithTimeOut(TimeSpan.FromSeconds(10))
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddVariable("outboundMessage", new ServiceBusMessage("Payload assembled at runtime. It is what it is.")
            {
                CorrelationId = CorrelationId,
                Subject = Subject,
            })
            .RunAsync();

        run.EnsureRanToCompletion();

        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist()
            .And().Match(m => m!.Subject == Subject, $"Subject must be '{Subject}'");
    }
}
