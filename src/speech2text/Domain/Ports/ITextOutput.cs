namespace speech2text.Domain.Ports;

/// <summary>
/// Injects transcribed text at the current cursor position in any active application,
/// as if the user had typed it from the keyboard.
/// This is the final step of a recording session — the domain triggers it after transcription completes.
/// </summary>
public interface ITextOutput
{
    Task InjectTextAsync(string text);
}
