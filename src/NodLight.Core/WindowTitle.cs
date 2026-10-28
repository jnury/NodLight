namespace NodLight.Core;

/// <summary>What the status glyph in front of an agent's window title says.</summary>
public enum TitleState { None, Idle, Working }

public static class WindowTitle
{
    /// <summary>
    /// Claude Code puts a status glyph in front of the terminal title: a four-pointed star (✳) when it is waiting
    /// for input and a spinner while it works: a half-filled disc (◐ ◓ ◑ ◒) or a braille dot pattern. The glyph is
    /// returned as <see cref="TitleState"/> and removed from the name; any other title comes back untouched.
    /// </summary>
    public static (string Name, TitleState State) Parse(string? title)
    {
        if (string.IsNullOrEmpty(title)) return ("", TitleState.None);

        var state = StateOf(title[0]);
        return state == TitleState.None ? (title, state) : (title[1..].Trim(), state);
    }

    static TitleState StateOf(char glyph) => glyph switch
    {
        '✳' => TitleState.Idle,
        >= '◐' and <= '◓' => TitleState.Working,
        >= '⠀' and <= '⣿' => TitleState.Working,
        _ => TitleState.None,
    };
}
