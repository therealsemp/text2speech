using System.Runtime.InteropServices;
using InputSimulatorStandard;
using InputSimulatorStandard.Native;
using speech2text.Domain.Ports;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;
using IDataObject = System.Windows.IDataObject;

namespace speech2text.Adapters.TextOutput;

/// <summary>
/// Injects text at the current cursor position by placing it on the clipboard and simulating
/// Ctrl+V, instead of typing it character by character. Faster and more reliable for long text
/// and applications that mangle synthetic keystrokes.
///
/// The clipboard content that was there before is snapshotted, then restored once the paste
/// has had time to complete — restoration happens unconditionally (whether or not the paste
/// actually landed in a text field), since a failed paste is instead recoverable from the
/// transcription history rather than from a preserved clipboard.
/// Windows-only constraint: relies on the WPF clipboard and the Win32 SendInput API.
/// </summary>
public class ClipboardPasteTextAdapter(IKeyboardSimulator? keyboard = null, TimeSpan? restoreDelay = null)
    : ITextOutput
{
    private const int RestoreRetryCount = 3;
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly IKeyboardSimulator _keyboard = keyboard ?? new InputSimulator().Keyboard;
    private readonly TimeSpan _restoreDelay = restoreDelay ?? TimeSpan.FromMilliseconds(250);

    public async Task InjectTextAsync(string text)
    {
        var previousClipboard = SnapshotClipboard();

        Clipboard.SetText(text);
        _keyboard.ModifiedKeyStroke(VirtualKeyCode.LCONTROL, VirtualKeyCode.VK_V);

        // Give the target application time to actually read the clipboard on paste
        // (some apps process Ctrl+V asynchronously) before we restore it underneath it.
        await Task.Delay(_restoreDelay);

        await RestoreClipboardAsync(previousClipboard);
    }

    /// <summary>
    /// Reads out every format currently on the clipboard into a standalone <see cref="DataObject"/>.
    /// This must happen eagerly: <see cref="Clipboard.GetDataObject"/> returns a proxy bound to the
    /// clipboard's current owner, which is no longer able to supply the data once we take over the
    /// clipboard with our own text right after — restoring that proxy later would leave the
    /// clipboard empty instead of putting the original content back.
    /// </summary>
    private static IDataObject? SnapshotClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            var formats = current?.GetFormats();
            if (formats == null || formats.Length == 0) return null;

            var snapshot = new DataObject();
            foreach (var format in formats)
            {
                try
                {
                    var data = current!.GetData(format);
                    if (data != null)
                        snapshot.SetData(format, data);
                }
                catch (COMException)
                {
                    // This format could not be rendered (e.g. its source app is no longer able
                    // to provide it) — skip it, restore what we can of the rest.
                }
            }
            return snapshot;
        }
        catch (COMException)
        {
            // Clipboard momentarily locked by another process — nothing to restore later.
            return null;
        }
    }

    private static async Task RestoreClipboardAsync(IDataObject? previousClipboard)
    {
        if (previousClipboard == null) return;

        for (var attempt = 1; attempt <= RestoreRetryCount; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(previousClipboard, true);
                return;
            }
            catch (COMException) when (attempt < RestoreRetryCount)
            {
                await Task.Delay(RestoreRetryDelay);
            }
        }
    }
}
