namespace Ble;

/// <summary>The modules ble-import knows (ble.sh names; contrib/ and .sh are accepted).</summary>
public static class BleModules
{
    public enum Result { NotBuiltin, Imported, ImportedNeedsFzf }

    static readonly string[] FzfKeymaps = ["emacs", "vi_imap", "vi_nmap"];

    public static Result TryImport(string name)
    {
        var key = name.Trim().Replace("contrib/", "integration/");
        if (key.EndsWith(".sh", StringComparison.Ordinal)) key = key.Substring(0, key.Length - 3);
        switch (key)
        {
            case "keymap.vi": case "vi":
                Options.Set("default_keymap", "vi");
                return Result.Imported;
            case "keymap.emacs": case "emacs":
                Options.Set("default_keymap", "emacs");
                return Result.Imported;
            case "integration/fzf-key-bindings": case "fzf-key-bindings":
                BindFzfKeys();
                return Result.ImportedNeedsFzf;
            case "integration/fzf-completion": case "fzf-completion":
                Options.Set("complete_fzf_trigger", "**");
                return Result.ImportedNeedsFzf;
            case "integration/fzf": case "fzf":
                BindFzfKeys();
                Options.Set("complete_fzf_trigger", "**");
                return Result.ImportedNeedsFzf;
        }
        return Result.NotBuiltin;
    }

    static void BindFzfKeys()
    {
        foreach (var km in FzfKeymaps)
        {
            Keymaps.Get(km).Bind(KeyNames.ParseSpec("C-t"), Binding.W("fzf-file-widget"));
            Keymaps.Get(km).Bind(KeyNames.ParseSpec("M-c"), Binding.W("fzf-cd-widget"));
            Keymaps.Get(km).Bind(KeyNames.ParseSpec("C-r"), Binding.W("fzf-history-widget"));
        }
    }
}
