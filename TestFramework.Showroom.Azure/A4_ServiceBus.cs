using Microsoft.Extensions.DependencyInjection;
using Azure.Messaging.ServiceBus;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: Send a message. Wait for reality to catch up.
//doc:
//doc: Service Bus is where the lane starts caring about time. Not just whether a message can be sent, but
//doc: whether the right message shows up later and can be identified without guesswork or wishful thinking.
//doc:
//doc: That is why correlation IDs are in every chapter here. Asynchronous systems are already generous with
//doc: ambiguity; the test does not need to add more and then act surprised. A wait that accepts *any*
//doc: message will happily accept the previous test's, and it will do it intermittently, which is the worst
//doc: available failure mode.
//doc:
//doc: All three chapters resolve to `components [azure-reset, servicebus-emulator]`. The queue and the topic
//doc: they use are declared in `AzureShowroom.cs`, along with the emulator topology that has to exist for
//doc: either to be addressable at all.

//doc: The basic asynchronous trust exercise: send one message to a topic, then wait for the exact correlated
//doc: receipt. Very simple. Emotionally difficult.
//doc:
//doc: Three modifiers on that wait, each pulling its weight. `correlationId:` is what makes the wait specific.
//doc: `completeMessage: true` acknowledges and removes the message once it has been observed - clean hands,
//doc: fewer ghosts. `GetMessage("out")` binds the received message into a variable, which is how it becomes
//doc: assertable afterwards. And `WithTimeOut(10s)` replaces the ten-minute default with something a
//doc: messaging test deserves: if nothing matching arrives in time, the run fails with a specific timeout and
//doc: no sympathy.

public class ServiceBus_SendAndReceive(ITestOutputHelper outputHelper)
{
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

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
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

//doc: The same timeline against a queue instead of a topic subscription, and the point is how little
//doc: changes: one identifier. Queue-versus-topic is a property of the declared resource, not of the
//doc: timeline, so a test written against one reads identically against the other.

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

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist().And().NotBeNull()
            .And().Match(m => m!.CorrelationId == CorrelationId, $"CorrelationId must be '{CorrelationId}'");
    }
}

//doc: And building the outbound message per run instead of baking it into the static timeline. The structure
//doc: stays fixed; the payload arrives through a variable, like every other per-run value since chapter 04.
//doc: Same structure, dynamic payload, fewer creative excuses.
//doc:
//doc: Notice that the correlation ID stays a constant here even though the message is now dynamic - not
//doc: because it has to be, but because nothing in this chapter needs it to vary. The wait takes a variable
//doc: reference just as happily, and in a suite where several runs share one namespace it should: chapter A6
//doc: hangs every identifier off a per-run id for exactly that reason.

public class ServiceBus_SendWithVariable(ITestOutputHelper outputHelper)
{
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

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
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
