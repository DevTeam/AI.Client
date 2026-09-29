namespace AI.Web.Composer;

/// <summary>The composer's text after the page changed it, and where the caret goes.</summary>
public sealed record ComposerTextEdit(string Text, int Caret);
