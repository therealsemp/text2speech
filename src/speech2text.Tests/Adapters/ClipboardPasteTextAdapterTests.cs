using InputSimulatorStandard;
using InputSimulatorStandard.Native;
using Moq;
using speech2text.Adapters.TextOutput;
using Clipboard = System.Windows.Clipboard;

namespace speech2text.Tests.Adapters;

/// <summary>
/// Exercises the clipboard snapshot/restore behavior directly against the real WPF clipboard
/// (requires an STA thread — provided by Xunit.StaFact). The keyboard simulator is mocked so
/// no real Ctrl+V keystroke is sent to the OS during tests.
/// </summary>
public class ClipboardPasteTextAdapterTests
{
    private readonly Mock<IKeyboardSimulator> _keyboard = new();

    private ClipboardPasteTextAdapter CreateAdapter() =>
        new(_keyboard.Object, restoreDelay: TimeSpan.FromMilliseconds(5));

    [StaFact]
    public async Task InjectTextAsync_RestoresPreviousClipboardContent()
    {
        Clipboard.SetText("previous content");

        await CreateAdapter().InjectTextAsync("transcribed text");

        Assert.Equal("previous content", Clipboard.GetText());
    }

    [StaFact]
    public async Task InjectTextAsync_WhenClipboardWasEmpty_LeavesTranscribedTextOnClipboard()
    {
        Clipboard.Clear();

        await CreateAdapter().InjectTextAsync("transcribed text");

        // Nothing to restore — the transcribed text stays, rather than being wiped to empty.
        Assert.Equal("transcribed text", Clipboard.GetText());
    }

    [StaFact]
    public async Task InjectTextAsync_SendsCtrlV()
    {
        Clipboard.SetText("previous content");

        await CreateAdapter().InjectTextAsync("transcribed text");

        _keyboard.Verify(x => x.ModifiedKeyStroke(VirtualKeyCode.LCONTROL, VirtualKeyCode.VK_V), Times.Once);
    }

    [StaFact]
    public async Task InjectTextAsync_PlacesTranscribedTextOnClipboard_BeforeRestoring()
    {
        Clipboard.SetText("previous content");
        string? clipboardDuringPaste = null;
        _keyboard.Setup(x => x.ModifiedKeyStroke(It.IsAny<VirtualKeyCode>(), It.IsAny<VirtualKeyCode>()))
            .Callback(() => clipboardDuringPaste = Clipboard.GetText());

        await CreateAdapter().InjectTextAsync("transcribed text");

        Assert.Equal("transcribed text", clipboardDuringPaste);
    }
}
