using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PresentationRemote.Presentation;

/// <summary>
/// Runs one whitelisted command at a time against the presentation controller,
/// after rate limiting. Never throws: errors become a <see cref="CommandResult"/>.
/// </summary>
public sealed class CommandRouter
{
    private readonly IPresentationController _controller;
    private readonly CommandRateLimiter _limiter;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public CommandRouter(IPresentationController controller, CommandRateLimiter? limiter = null, ILogger<CommandRouter>? logger = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _limiter = limiter ?? new CommandRateLimiter();
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public CommandResult Execute(PresentationCommand command)
    {
        lock (_gate)
        {
            var decision = _limiter.TryAcquire(command);
            if (decision != RateLimitDecision.Allowed)
            {
                _logger.LogDebug("Dropped {Command}: {Decision}", command, decision);
                return CommandResult.RateLimited(decision == RateLimitDecision.Duplicate
                    ? "Ignored a double tap."
                    : "Too many taps. Wait a moment.");
            }

            CommandResult result;
            try
            {
                result = command switch
                {
                    PresentationCommand.Next => _controller.Next(),
                    PresentationCommand.Previous => _controller.Previous(),
                    PresentationCommand.Start => _controller.Start(),
                    PresentationCommand.ToggleBlack => _controller.ToggleBlack(),
                    PresentationCommand.End => _controller.End(),
                    _ => CommandResult.Rejected("Unsupported command."),
                };
            }
#pragma warning disable CA1031 // A failing controller must never take the server down.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Presentation controller threw while handling {Command}", command);
                result = CommandResult.Failed("The computer could not send that command.");
            }

            _logger.LogInformation("{Command} -> {Outcome}", command, result.Outcome);
            return result;
        }
    }
}
