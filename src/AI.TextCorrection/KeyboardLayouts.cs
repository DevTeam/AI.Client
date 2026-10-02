namespace AI.TextCorrection;

public sealed class KeyboardLayouts : IKeyboardLayouts
{
    public IReadOnlyList<KeyboardLayout> All { get; } =
    [
        new("en", "English (US)", "`qwertyuiop[]asdfghjkl;'zxcvbnm,./1234567890-=", "~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?!@#$%^&*()_+"),
        new("ru", "Русская", "ёйцукенгшщзхъфывапролджэячсмитьбю.1234567890-=", "ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,!\"№;%:?*()_+"),
        new("fr", "Français (AZERTY)", "²azertyuiop^$qsdfghjklmùwxcvbn,;:!&é\"'(-è_çà)=", "~AZERTYUIOP¨£QSDFGHJKLM%WXCVBN?./§1234567890°+"),
        new("es", "Español (España)", "ºqwertyuiop`+asdfghjklñ´zxcvbnm,.-1234567890'¡", "ªQWERTYUIOP^*ASDFGHJKLÑ¨ZXCVBNM;:_!\"·$%&/()=?¿"),
    ];
}
