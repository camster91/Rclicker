using RClicker.Presentation;
using RClicker.Tests.TestDoubles;

namespace RClicker.Tests.Presentation;

public class CommandRouterTests
{
    [Theory]
    [InlineData(PresentationCommand.Next)]
    [InlineData(PresentationCommand.Previous)]
    [InlineData(PresentationCommand.Start)]
    [InlineData(PresentationCommand.ToggleBlack)]
    [InlineData(PresentationCommand.End)]
    public void Execute_CallsTheMatchingControllerAction_ExactlyOnce(PresentationCommand command)
    {
        var controller = new FakePresentationController();
        var router = new CommandRouter(controller, new CommandRateLimiter(new ManualTimeProvider()));

        var result = router.Execute(command);

        Assert.True(result.IsSuccess);
        Assert.Equal([command], controller.Calls);
    }

    [Fact]
    public void RapidDoubleTap_ResultsInOneAction()
    {
        var clock = new ManualTimeProvider();
        var controller = new FakePresentationController();
        var router = new CommandRouter(controller, new CommandRateLimiter(clock));

        var first = router.Execute(PresentationCommand.Next);
        clock.Advance(TimeSpan.FromMilliseconds(30));
        var second = router.Execute(PresentationCommand.Next);

        Assert.Equal(CommandOutcome.Executed, first.Outcome);
        Assert.Equal(CommandOutcome.RateLimited, second.Outcome);
        Assert.Equal(1, controller.Count(PresentationCommand.Next));
    }

    [Fact]
    public void ControllerException_BecomesFailedResult()
    {
        var controller = new FakePresentationController { Behaviour = _ => throw new InvalidOperationException("boom") };
        var router = new CommandRouter(controller, new CommandRateLimiter(new ManualTimeProvider()));

        var result = router.Execute(PresentationCommand.Next);

        Assert.Equal(CommandOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("boom", result.Message, StringComparison.Ordinal); // No internals to the phone.
    }

    [Fact]
    public async Task ConcurrentCommands_AreSerialisedAndRateLimited()
    {
        var controller = new FakePresentationController();
        var router = new CommandRouter(controller, new CommandRateLimiter(new ManualTimeProvider()));

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => router.Execute(PresentationCommand.Next))));

        Assert.Equal(1, controller.Count(PresentationCommand.Next)); // Clock never moved: all others are duplicates.
    }
}
