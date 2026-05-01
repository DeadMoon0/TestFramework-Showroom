using Azure.Messaging.ServiceBus;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION — PARTICIPANT ORIENTATION MODULE A4
//  "Service Bus: Asynchronous Messaging For The Chronologically Flexible"
//
//  Service Bus is a message broker. You send a message.
//  Something else picks it up. Maybe immediately. Maybe after a brief think.
//  The Timeline framework can wait for that message on your behalf.
//  It is a patient framework. We trained it ourselves over many, many builds.
//
//  IMPORTANT: The message identifiers in these examples are "MainSBQueue" and "MainSBTopic."
//  Their queue/topic config comes from the shared Azure showroom container setup.
//  If they don't match, the WaitForEvent step will time out.
//  The timeout window is 10 seconds. The error message is clear.
//  The situation is manageable. We have managed worse situations.
//  We don't want to talk about those situations.
// ══════════════════════════════════════════════════════════════════════════════

[Collection("AzureShowroom")]
public class ServiceBus_SendAndReceive(ITestOutputHelper outputHelper)
{
    // The classic "did it actually go?" test.
    // SEND a message. WAIT for the event that fires when it is received.
    // Correlate using a CorrelationId so you know it's YOUR message coming back.
    // This is important. Other messages exist on the bus. They are not yours.
    // Do not assert on messages that are not yours. We've seen what that leads to.

    private const string CorrelationId = "showroom-42";

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureTF.Trigger.ServiceBus.Send("MainSBTopic", new ServiceBusMessage("Live transmission. Please stand by.") { CorrelationId = CorrelationId }))
        //  ^ SEND a message with a known CorrelationId.
        //    The CorrelationId is how we'll recognise the reply.
        //    It's like writing your name on your lunch in the communal fridge.
        //    Works fine until someone eats it anyway. These are Azure messages. Nobody eats them.
        .WaitForEvent(AzureTF.Event.ServiceBus.MessageReceived("MainSBTopic", correlationId: CorrelationId, completeMessage: true))
            // complete = acknowledge + remove. Clean hands.
            .WithTimeOut(TimeSpan.FromSeconds(10))
        //  ^ Wait up to 10 seconds. If nothing arrives, the step fails.
        //    10 seconds felt generous. It felt less generous after the first few timeouts.
        //    We kept it. Character builds.
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        // The received message is stored as a variable named "out" by the WaitForEvent step.
        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist().And().NotBeNull()
            .And().Match(m => m!.CorrelationId == CorrelationId, $"CorrelationId must be '{CorrelationId}'");
        // ^ Confirm it was YOUR message that arrived, not someone else's lunch.
    }
}

[Collection("AzureShowroom")]
public class ServiceBus_QueueSendAndReceive(ITestOutputHelper outputHelper)
{
    private const string CorrelationId = "showroom-queue-42";

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureTF.Trigger.ServiceBus.Send("MainSBQueue", new ServiceBusMessage("Queue delivery. Clean and direct.") { CorrelationId = CorrelationId }))
        .WaitForEvent(AzureTF.Event.ServiceBus.MessageReceived("MainSBQueue", correlationId: CorrelationId, completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(10))
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist().And().NotBeNull()
            .And().Match(m => m!.CorrelationId == CorrelationId, $"CorrelationId must be '{CorrelationId}'");
    }
}

[Collection("AzureShowroom")]
public class ServiceBus_SendWithVariable(ITestOutputHelper outputHelper)
{
    // If your message content is dynamic — built at test setup time, values you only know
    // at runtime — use Var.Ref to pass it in by reference.
    //
    // The Timeline is built statically (once, at class load).
    // The actual message is injected per-run.
    // Clean separation. Very professional.
    // We clean up our separations. We are professional.

    private const string CorrelationId = "showroom-dynamic";
    private const string Subject = "Showroom Test";

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(AzureTF.Trigger.ServiceBus.Send("MainSBTopic", Var.Ref<ServiceBusMessage>("outboundMessage")))
        //    ^ Notice the Var.Ref here. The message isn't known until RunAsync time.
        //      By the time you read this comment, the message will have been provided.
        //      The future is already written. Mostly.
        .WaitForEvent(AzureTF.Event.ServiceBus.MessageReceived("MainSBTopic", correlationId: CorrelationId, completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(10))
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
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
