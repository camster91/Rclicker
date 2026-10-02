using System.Collections.Concurrent;
using RClicker.Presentation;

namespace RClicker.Tests.TestDoubles;

/// <summary>Records calls instead of pressing keys. Never touches the real keyboard.</summary>
internal sealed class FakePresentationController : IPresentationController
{
    private readonly ConcurrentQueue<PresentationCommand> _calls = new();

    public Func<PresentationCommand, CommandResult>? Behaviour { get; set; }

    public IReadOnlyList<PresentationCommand> Calls => _calls.ToArray();

    public int Count(PresentationCommand command) => _calls.Count(c => c == command);

    public CommandResult Next() => Record(PresentationCommand.Next);

    public CommandResult Previous() => Record(PresentationCommand.Previous);

    public CommandResult Start() => Record(PresentationCommand.Start);

    public CommandResult ToggleBlack() => Record(PresentationCommand.ToggleBlack);

    public CommandResult End() => Record(PresentationCommand.End);

    private CommandResult Record(PresentationCommand command)
    {
        _calls.Enqueue(command);
        return Behaviour?.Invoke(command) ?? CommandResult.Executed;
    }
}

internal sealed class FakeKeySender : IKeySender
{
    public List<PresentationKey> Sent { get; } = [];

    public string? FailWith { get; set; }

    public bool TrySend(PresentationKey key, out string? failureReason)
    {
        if (FailWith is not null)
        {
            failureReason = FailWith;
            return false;
        }

        Sent.Add(key);
        failureReason = null;
        return true;
    }
}

internal sealed class FakeForegroundWindow : IForegroundWindowProvider
{
    public ForegroundWindowInfo Current { get; set; } = new("POWERPNT", PowerPointTargetPolicy.SlideShowWindowClass);

    public ForegroundWindowInfo GetForegroundWindow() => Current;
}
