using InputSimulatorStandard;
using speech2text.Domain.Ports;

namespace speech2text.Adapters.TextOutput;

/// <summary>
/// Injects text at the current cursor position using the InputSimulatorStandard library,
/// which wraps the Win32 SendInput API. Text is sent as a sequence of Unicode character events,
/// making it compatible with virtually any Windows application regardless of its input handling.
/// Windows-only constraint: SendInput is a Win32 API.
/// </summary>
public class SendInputTextAdapter : ITextOutput
{
    private readonly InputSimulator _simulator = new();

    public Task InjectTextAsync(string text)
    {
        _simulator.Keyboard.TextEntry(text);
        return Task.CompletedTask;
    }
}
