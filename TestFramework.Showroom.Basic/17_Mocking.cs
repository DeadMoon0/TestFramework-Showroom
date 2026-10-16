using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Mock;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Recording;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Every chapter so far drove something from the outside: a command, a file, a message. This one goes
//doc: inside. `TestFramework.Mock` hosts the system under test in the test process - your own service
//doc: composition, the registrations production makes - with chosen dependencies replaced by declared
//doc: test doubles. No Docker, no browser, no port. Just the code, and a double where the network used
//doc: to be.
//doc:
//doc: Three pieces, each with one job:
//doc:
//doc: 1. A **pack** - `MockPack<TService>` - declares one double, once: which calls it answers and how.
//doc: 2. `MockEnvironment.For(...)` hosts the composition, with each included pack standing where the real
//doc:    dependency was registered. It is an environment like any other, set with `SetEnv`.
//doc: 3. `MockExt.Host(...)` is the timeline step that calls into the hosted service.
//doc:
//doc: A double never writes into the run. It records, the way a real dependency leaves a file behind, and
//doc: the run brings that in with Core's ordinary `FindArtifact`. That rule is the whole of chapter three
//doc: below, and it is why nothing here needs a new verb on the run.

//doc: The system under test first. It is deliberately small: a signup service that keeps a member list
//doc: and sends a welcome mail. `AddSignup` is the composition production would call, and the chapter
//doc: hands it over as it is rather than rebuilding it for the test - the test should host what ships.
//doc:
//doc: Note the real mail sender. Its constructor refuses to exist outside production, which makes it a
//doc: convenient witness: if any test below so much as constructs it, that test fails.

public interface IMailSender
{
    Task SendAsync(string to, string subject);
}

public interface IMemberDirectory
{
    bool TryAdd(string email);
}

public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string text);
}

public sealed class SmtpMailSender : IMailSender
{
    public SmtpMailSender() => throw new InvalidOperationException("There is no SMTP server in a showroom. Something constructed the real mail sender.");

    public Task SendAsync(string to, string subject) => throw new InvalidOperationException("Unreachable: the constructor already refused.");
}

public sealed class InMemoryMemberDirectory : IMemberDirectory
{
    private readonly HashSet<string> _members = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(string email) => this._members.Add(email);
}

public sealed class SignupService(IMemberDirectory members, IMailSender mail)
{
    public async Task<bool> RegisterAsync(string email, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!members.TryAdd(email))
        {
            return false;
        }

        await mail.SendAsync(email, "Welcome");
        return true;
    }
}

public static class SignupComposition
{
    public static IServiceCollection AddSignup(this IServiceCollection services)
        => services
            .AddSingleton<IMemberDirectory, InMemoryMemberDirectory>()
            .AddSingleton<IMailSender, SmtpMailSender>()
            .AddSingleton<SignupService>();
}

//doc: The pack. One setup: any address, the subject "Welcome", and the call completes. The mock is strict,
//doc: so a call the pack does not declare is refused by name rather than answered with a shrug and a
//doc: default. `ProducesArtifact` is chapter three's business; it records the mail once the call has
//doc: completed, and stays out of the way until somebody looks.

public sealed record WelcomeMail(string To, string Subject);

public sealed class MailSenderPack : MockPack<IMailSender>
{
    protected override void Configure(MockBuilder<IMailSender> mock)
    {
        mock.Call(m => m.SendAsync(MockArg.Any<string>(), "Welcome"))
            .Completes()
            .ProducesArtifact((string to, string subject) => new("welcomeMail", new WelcomeMail(to, subject)));
    }
}

public sealed class SmsSenderPack : MockPack<ISmsSender>
{
    protected override void Configure(MockBuilder<ISmsSender> mock)
    {
        mock.Call(s => s.SendAsync(MockArg.Any<string>(), MockArg.Any<string>())).Completes();
    }
}

//doc: Chapter one: replace one dependency, keep the rest. The member directory is the real one; only the
//doc: mail sender is a double, because `Include<MailSenderPack>()` removed every registration of
//doc: `IMailSender` before adding it. The real sender is never constructed - its constructor would have
//doc: said so loudly.
//doc:
//doc: Two different questions get two different reads. `HostResult<bool>("register")` is what the system
//doc: under test answered, read by the step's label. `CountCalls(...)` is what it did to its dependency,
//doc: matched the same way the pack matches: exact values where the test cares, `MockArg.Any<T>()` where
//doc: it does not.

public class Mocking(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Label("register")
        .Build();

    [Fact]
    public async Task Run_ReplacesOneDependencyAndKeepsTheRest()
    {
        MockEnvironment environment = MockEnvironment.For(services => services.AddSignup())
            .Include<MailSenderPack>();

        TimelineRun run = await this._timeline.SetupRun(outputHelper.WriteLine)
            .SetEnv(environment)
            .RunAsync();
        run.EnsureRanToCompletion();

        Assert.True(run.HostResult<bool>("register"));
        Assert.Equal(1, run.Mock<IMailSender>().CountCalls(m => m.SendAsync("ada@example.com", MockArg.Any<string>())));
        //                                ^ The double, frozen with its run. The call log does not change after the
        //                                  run finishes, so an assertion made tomorrow reads what happened today.
    }
}

//doc: Chapter two: the same service, called twice with the same address, which is the case a signup has
//doc: to get right. The address comes from a run variable - `Host` takes the call's own arguments first
//doc: and the hosted service behind them - so the timeline is a structure and the address is an input,
//doc: exactly as in chapter 04.
//doc:
//doc: The second call also takes the step's `CancellationToken`, added behind the service. It is cancelled
//doc: when the step times out or the run is stopped, so a call that honours it stops with its step instead
//doc: of outliving it.
//doc:
//doc: The member directory is a singleton, shared across the run, so the second registration finds the
//doc: first and declines. One mail, not two. `RecordedCalls` shows the log behind the count: every call in
//doc: order, with its arguments and whether a setup matched it.

public class Mocking_Variables(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(MockExt.Host(
            Var.Ref<string>("email"),
            (string email, SignupService signup) => signup.RegisterAsync(email))).Label("first")
        .Trigger(MockExt.Host(
            Var.Ref<string>("email"),
            (string email, SignupService signup, CancellationToken cancellation) => signup.RegisterAsync(email, cancellation))).Label("again")
        .Build();

    [Fact]
    public async Task Run_SendsOneMailForTwoRegistrations()
    {
        MockEnvironment environment = MockEnvironment.For(services => services.AddSignup())
            .Include<MailSenderPack>();

        TimelineRun run = await this._timeline.SetupRun(outputHelper.WriteLine)
            .AddVariable("email", "grace@example.com")
            .SetEnv(environment)
            .RunAsync();
        run.EnsureRanToCompletion();

        Assert.True(run.HostResult<bool>("first"));
        Assert.False(run.HostResult<bool>("again"));

        RecordedCall call = Assert.Single(run.Mock<IMailSender>().RecordedCalls);
        Assert.Equal(nameof(IMailSender.SendAsync), call.Method.Name);
        Assert.Equal(new object?[] { "grace@example.com", "Welcome" }, call.Arguments);
        Assert.True(call.Matched);
    }
}

//doc: Chapter three: evidence. The pack's `ProducesArtifact` recorded a `WelcomeMail` under the identity
//doc: `welcomeMail` when the send completed. That record sits in the environment - the double's outbox,
//doc: if you like - until a step looks, and the step is `FindArtifact` with a `MockArtifactFinder`. Same
//doc: verb that finds a file another program wrote in chapter 05; the double is just another program.
//doc:
//doc: In the report below, the Find Artifact step lists the mail it found. A call that threw would have
//doc: published nothing: an artifact is what a *completed* call produced, not what was attempted.

public class Mocking_Artifacts(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("hedy@example.com"))).Label("register")
        .FindArtifact("welcomeMail", new MockArtifactFinder("welcomeMail"))
        .Build();

    [Fact]
    public async Task Run_FindsTheMailTheDoubleRecorded()
    {
        MockEnvironment environment = MockEnvironment.For(services => services.AddSignup())
            .Include<MailSenderPack>();

        TimelineRun run = await this._timeline.SetupRun(outputHelper.WriteLine)
            .SetEnv(environment)
            .RunAsync();
        run.EnsureRanToCompletion();

        object? payload = run.ArtifactStore.GetMockArtifact("welcomeMail").Last.Payload;
        Assert.Equal(new WelcomeMail("hedy@example.com", "Welcome"), payload);
    }
}

//doc: Chapter four is the refusal, because a double that stands in for nothing is the most comfortable
//doc: lie a test suite can tell. Here the environment includes an `SmsSenderPack`, but the signup
//doc: composition never registers an `ISmsSender` - perhaps it once did, perhaps somebody renamed it. A
//doc: double in that position would answer no calls, every count would read zero, and a test asserting
//doc: "no SMS was sent" would pass forever.
//doc:
//doc: So it is refused when the run is planned, before the first step, naming the pack and the service.
//doc: The recovery steps say what to check and offer the other door: `IncludeAdditional<SmsSenderPack>()`
//doc: is for a double that is *meant* to add a service the composition lacks. It is a different statement,
//doc: so it is a different method - and `IncludeAdditional` for a service that *is* registered is refused
//doc: in turn, pointing back at `Include`.
//doc:
//doc: As in chapter 15, the run never started, so there is no report. The panel shows the refusal instead.

public class Mocking_UnregisteredService(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("alan@example.com"))).Label("register")
        .Build();

    [Fact]
    public async Task Run_RefusesAPackThatStandsInForNothing()
    {
        MockEnvironment environment = MockEnvironment.For(services => services.AddSignup())
            .Include<MailSenderPack>()
            .Include<SmsSenderPack>();

        FrameworkConfigurationException refusal = await Assert.ThrowsAsync<FrameworkConfigurationException>(() =>
            this._timeline.SetupRun(outputHelper.WriteLine).SetEnv(environment).RunAsync());

        outputHelper.WriteLine(refusal.Message);
        //                         ^ The message carries its recovery steps; the panel shows it as a reader would meet it.

        Assert.Contains(nameof(ISmsSender), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(refusal.RecoverySteps, step => step.Contains("IncludeAdditional<SmsSenderPack>()", StringComparison.Ordinal));
    }
}
