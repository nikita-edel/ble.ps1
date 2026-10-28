using System.Management.Automation;

namespace Ble;

public enum BindingKind { Widget, Command, Edit, Macro, UserWidget }

public sealed record Binding(BindingKind Kind, string Widget, ScriptBlock? Script = null)
{
    public static Binding W(string widget) => new(BindingKind.Widget, widget);

    public string Describe() => Kind switch
    {
        BindingKind.Widget => Widget,
        BindingKind.Command => "-c {" + Script + "}",
        BindingKind.Macro => "-s '" + Widget + "'",
        BindingKind.UserWidget => Widget,
        _ => "-x {" + Script + "}",
    };
}

public sealed class Keymap
{
    public string Name { get; }
    /// <summary>When true, an unbound 'ESC x' sequence is looked up as 'M-x'.</summary>
    public bool EscapeIsMeta { get; init; }
    readonly Dictionary<string, Binding> _map = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _prefixes = new(StringComparer.Ordinal);

    public Keymap(string name) { Name = name; }

    public IEnumerable<KeyValuePair<string, Binding>> Bindings => _map.OrderBy(k => k.Key, StringComparer.Ordinal);

    public void Bind(string[] seq, Binding? binding)
    {
        var key = string.Join(' ', seq);
        bool had = _map.ContainsKey(key);
        if (binding == null)
        {
            if (had) { _map.Remove(key); UpdatePrefixes(seq, -1); }
            return;
        }
        _map[key] = binding;
        if (!had) UpdatePrefixes(seq, +1);
    }

    public void Bind(string spec, string widget) => Bind(KeyNames.ParseSpec(spec), Binding.W(widget));

    void UpdatePrefixes(string[] seq, int delta)
    {
        for (int n = 1; n < seq.Length; n++)
        {
            var p = string.Join(' ', seq, 0, n);
            _prefixes.TryGetValue(p, out var c);
            c += delta;
            if (c <= 0) _prefixes.Remove(p); else _prefixes[p] = c;
        }
    }

    public bool TryGet(string seq, out Binding binding) => _map.TryGetValue(seq, out binding!);
    public bool IsPrefix(string seq) => _prefixes.ContainsKey(seq) || (EscapeIsMeta && seq == "ESC");
    public void Clear() { _map.Clear(); _prefixes.Clear(); }
}

public static class Keymaps
{
    static readonly Dictionary<string, Keymap> Maps = new(StringComparer.Ordinal);

    public static readonly string[] Names = ["emacs", "vi_imap", "vi_nmap", "vi_xmap", "vi_smap"];

    static Keymaps() { Reset(); }

    public static Keymap Get(string name)
    {
        if (Maps.TryGetValue(name, out var km)) return km;
        throw new ArgumentException($"unknown keymap '{name}' (available: {string.Join(", ", Names)})");
    }

    public static bool Exists(string name) => Maps.ContainsKey(name);

    public static void Reset()
    {
        Maps.Clear();
        var emacs = new Keymap("emacs") { EscapeIsMeta = true };
        var imap = new Keymap("vi_imap");
        Maps["emacs"] = emacs;
        Maps["vi_imap"] = imap;
        Maps["vi_nmap"] = new Keymap("vi_nmap");
        Maps["vi_xmap"] = new Keymap("vi_xmap");
        Maps["vi_smap"] = new Keymap("vi_smap");
        // same structure as ble.sh: lib/keymap.emacs.sh (emacs/define) and lib/keymap.vi.sh (vi_imap/define)
        BindCommon(emacs, meta: true);
        BindHistory(emacs);
        BindComplete(emacs);
        BindArg(emacs, meta: true);
        BindEmacs(emacs);
        BindCommon(imap, meta: false);
        BindHistory(imap);
        BindComplete(imap);
        BindArg(imap, meta: false);
        BindViInsert(imap);
        BindViInsertMeta(imap);
        BindBlePs1Extras(emacs);
        BindBlePs1Extras(imap);
    }

    static void B(Keymap k, string widget, params string[] keys)
    {
        foreach (var key in keys) k.Bind(key, widget);
    }

    // ble.sh src/edit.sh: ble-decode/keymap:safe/bind-common (+ the shift-selection "@marked" bindings)
    static void BindCommon(Keymap k, bool meta)
    {
        B(k, "overwrite-mode", "insert");
        B(k, "quoted-insert", "C-q", "C-v");
        B(k, "set-mark", "C-@", "C-SP");
        B(k, "exchange-point-and-mark", "C-x C-x");
        B(k, "kill-region-or-backward-uword", "C-w", "S-delete");
        B(k, "copy-region-or-backward-uword", "C-insert");
        B(k, "yank", "C-y", "S-insert");
        B(k, "forward-char", "C-f", "right");
        B(k, "backward-char", "C-b", "left");
        B(k, "delete-forward-char-or-exit", "C-d");
        B(k, "delete-forward-char", "delete");
        B(k, "delete-backward-char", "DEL", "C-h");
        B(k, "transpose-chars", "C-t");
        B(k, "forward-cword", "C-right");
        B(k, "backward-cword", "C-left");
        B(k, "delete-forward-cword", "C-delete");
        B(k, "delete-backward-cword", "C-_", "C-DEL");
        B(k, "beginning-of-line", "C-a", "home");
        B(k, "end-of-line", "C-e", "end");
        B(k, "kill-forward-line", "C-k");
        B(k, "kill-backward-line", "C-u");
        B(k, "beginning-of-text", "C-home");
        B(k, "end-of-text", "C-end");
        B(k, "start-keyboard-macro", "C-x (");
        B(k, "end-keyboard-macro", "C-x )");
        B(k, "call-keyboard-macro", "C-x e");
        B(k, "print-keyboard-macro", "C-x P");
        B(k, "character-search-forward", "C-]");
        B(k, "shift-forward-char", "S-C-f", "S-right");
        B(k, "shift-backward-char", "S-C-b", "S-left");
        B(k, "shift-forward-cword", "S-C-right");
        B(k, "shift-backward-cword", "S-C-left");
        B(k, "shift-beginning-of-line", "S-C-a", "S-home");
        B(k, "shift-end-of-line", "S-C-e", "S-end");
        B(k, "shift-backward-line", "S-C-p", "S-up");
        B(k, "shift-forward-line", "S-C-n", "S-down");
        B(k, "shift-beginning-of-text", "S-C-home");
        B(k, "shift-end-of-text", "S-C-end");
        if (!meta) return;
        B(k, "newline", "M-C-m", "M-RET");
        B(k, "set-mark", "M-SP");
        B(k, "copy-region-or-backward-uword", "M-w");
        B(k, "yank-pop", "M-y");
        B(k, "yank-pop-backward", "M-S-y", "M-Y");
        B(k, "delete-horizontal-space", "M-\\");
        B(k, "forward-sword", "M-right");
        B(k, "backward-sword", "M-left");
        B(k, "kill-forward-cword", "M-d");
        B(k, "kill-backward-cword", "M-h");
        B(k, "copy-forward-sword", "M-delete");
        B(k, "copy-backward-sword", "M-DEL", "M-C-h");
        B(k, "forward-cword", "M-f");
        B(k, "backward-cword", "M-b");
        B(k, "capitalize-word", "M-c");
        B(k, "downcase-word", "M-l");
        B(k, "upcase-word", "M-u");
        B(k, "transpose-words", "M-t");
        B(k, "non-space-beginning-of-line", "M-m");
        B(k, "character-search-backward", "M-C-]");
        B(k, "execute-named-command", "M-x");
        B(k, "shift-forward-cword", "M-F", "M-S-f");
        B(k, "shift-backward-cword", "M-B", "M-S-b");
        B(k, "shift-forward-sword", "M-S-right");
        B(k, "shift-backward-sword", "M-S-left");
        B(k, "shift-non-space-beginning-of-line", "M-S-m", "M-M");
    }

    // ble.sh: ble-decode/keymap:safe/bind-history
    static void BindHistory(Keymap k)
    {
        B(k, "history-isearch-backward", "C-r");
        B(k, "history-isearch-forward", "C-s");
        B(k, "history-beginning", "M-<", "C-prior");
        B(k, "history-end", "M->", "C-next");
        B(k, "history-prev", "C-up");
        B(k, "history-next", "C-down");
        B(k, "backward-line-or-history-prev", "C-p", "up");
        B(k, "forward-line-or-history-next", "C-n", "down");
        B(k, "history-search-backward", "prior", "C-x C-p", "C-x up");
        B(k, "history-search-forward", "next", "C-x C-n", "C-x down");
        B(k, "history-substring-search-backward", "C-x p");
        B(k, "history-substring-search-forward", "C-x n");
        B(k, "history-nsearch-backward", "C-x <");
        B(k, "history-nsearch-forward", "C-x >");
        B(k, "history-nsearch-backward-again", "C-x ,");
        B(k, "history-nsearch-forward-again", "C-x .");
        B(k, "insert-last-argument", "M-.", "M-_");
        B(k, "insert-nth-argument", "M-C-y");
    }

    // ble.sh: ble-decode/keymap:safe/bind-complete
    static void BindComplete(Keymap k)
    {
        B(k, "complete", "TAB");
        B(k, "possible-completions", "M-?");
        B(k, "insert-completions", "M-*");
        B(k, "menu-complete", "C-TAB");
        B(k, "menu-complete-backward", "S-TAB");
        B(k, "complete-filename", "M-/");
        B(k, "complete-variable", "M-$");
        B(k, "complete-command", "M-!");
        B(k, "complete-filename-menu", "C-x /");
        B(k, "complete-variable-menu", "C-x $");
        B(k, "complete-command-menu", "C-x !");
        B(k, "sabbrev-expand", "M-'", "C-x '");
        B(k, "dabbrev-expand", "C-x C-r");
        B(k, "complete-glob", "M-g");
        B(k, "complete-glob-insert-all", "C-x *");
        B(k, "complete-glob-menu", "C-x g");
        B(k, "complete-dynamic-history", "M-C-i", "M-TAB");
    }

    // ble.sh: ble-decode/keymap:safe/bind-arg (digits continue an argument started with M-digit / M-C-u)
    static void BindArg(Keymap k, bool meta)
    {
        if (meta)
        {
            B(k, "universal-arg", "M-C-u");
            B(k, "digit-argument", "M--");
            for (int d = 0; d <= 9; d++) B(k, "digit-argument", "M-" + d);
        }
        B(k, "digit-argument", "C--");
        for (int d = 0; d <= 9; d++)
        {
            B(k, "digit-argument", "C-" + d);
            B(k, "append-arg", d.ToString());
        }
        B(k, "append-arg", "-");
    }

    // ble.sh lib/keymap.emacs.sh: ble-decode/keymap:emacs/define
    static void BindEmacs(Keymap k)
    {
        B(k, "delete-forward-char-or-exit", "C-d");
        B(k, "magic-space", "SP");
        B(k, "discard-line", "C-c");
        B(k, "accept-suggestion-and-execute", "C-j", "C-RET");    // accept-line (+ auto_complete keymap)
        B(k, "accept-single-line-or-newline", "RET");
        B(k, "accept-and-next", "C-o");
        B(k, "edit-and-execute-command", "C-x C-e");
        B(k, "insert-comment", "M-#");
        B(k, "shell-expand-line", "M-C-e");
        B(k, "tilde-expand", "M-&");
        B(k, "cancel", "C-g", "C-x C-g", "C-M-g");               // bell (+ cancels mark/menu/suggestion)
        B(k, "clear-screen", "C-l");
        B(k, "redraw-line", "C-M-l");
        B(k, "command-help", "f1");
        B(k, "toggle-suggestion-view", "f2");                    // ble.ps1 (PSReadLine's F2)
        B(k, "display-shell-version", "C-x C-v");
        B(k, "zap-to-char", "M-z");
        B(k, "bell", "C-\\", "C-^");
        B(k, "undo", "C-_", "C-DEL", "C-/", "C-x u", "C-x C-u");
        B(k, "redo", "C-x U", "C-x C-S-u");
        B(k, "revert-line", "M-r");
        B(k, "quoted-insert", "C-q", "C-v");
    }

    // ble.sh lib/keymap.vi.sh: ble-decode/keymap:vi_imap/define
    static void BindViInsert(Keymap k)
    {
        B(k, "delete-forward-char-or-exit", "C-d");
        B(k, "magic-space", "SP");
        B(k, "accept-suggestion-and-execute", "C-j", "C-RET");
        B(k, "accept-single-line-or-newline", "RET");
        B(k, "edit-and-execute-command", "C-x C-e");
        B(k, "cancel", "C-g", "C-x C-g");
        B(k, "clear-screen", "C-l");
        B(k, "command-help", "f1");
        B(k, "toggle-suggestion-view", "f2");                    // ble.ps1 (PSReadLine's F2)
        B(k, "display-shell-version", "C-x C-v");
        B(k, "overwrite-mode", "insert");
        B(k, "quoted-insert", "C-q", "C-v");
        B(k, "vi_imap/delete-backward-char", "DEL", "C-h");
        B(k, "vi_imap/kill-backward-word", "C-w");
        B(k, "bell", "C-\\", "C-^");
        B(k, "vi-normal-mode", "ESC");
        B(k, "vi_imap/normal-mode-without-insert-leave", "C-c");
        B(k, "vi-insert-one-command", "C-o");
    }

    // ble.sh: ble-decode/keymap:vi_imap/define-meta-bindings
    static void BindViInsertMeta(Keymap k)
    {
        B(k, "redraw-line", "C-M-l");
        B(k, "insert-comment", "M-#");
        B(k, "shell-expand-line", "M-C-e");
        B(k, "tilde-expand", "M-&");
        B(k, "bell", "C-M-g");
        B(k, "zap-to-char", "M-z");
        B(k, "newline", "M-C-m", "M-RET");
        B(k, "set-mark", "M-SP");
        B(k, "copy-region-or-backward-uword", "M-w");
        B(k, "yank-pop", "M-y");
        B(k, "yank-pop-backward", "M-S-y", "M-Y");
        B(k, "delete-horizontal-space", "M-\\");
        B(k, "forward-sword", "M-right");
        B(k, "backward-sword", "M-left");
        B(k, "shift-forward-sword", "S-M-right");
        B(k, "shift-backward-sword", "S-M-left");
        B(k, "kill-forward-cword", "M-d");
        B(k, "kill-backward-cword", "M-h");
        B(k, "copy-forward-sword", "M-delete");
        B(k, "copy-backward-sword", "M-DEL", "M-C-h");
        B(k, "forward-cword", "M-f");
        B(k, "backward-cword", "M-b");
        B(k, "shift-forward-cword", "M-F", "M-S-f");
        B(k, "shift-backward-cword", "M-B", "M-S-b");
        B(k, "capitalize-word", "M-c");
        B(k, "downcase-word", "M-l");
        B(k, "upcase-word", "M-u");
        B(k, "transpose-words", "M-t");
        B(k, "non-space-beginning-of-line", "M-m");
        B(k, "shift-non-space-beginning-of-line", "M-S-m", "M-M");
        B(k, "execute-named-command", "M-x");
        B(k, "history-beginning", "M-<");
        B(k, "history-end", "M->");
        B(k, "insert-last-argument", "M-.", "M-_");
        B(k, "insert-nth-argument", "M-C-y");
        B(k, "possible-completions", "M-?");
        B(k, "insert-completions", "M-*");
        B(k, "complete-filename", "M-/");
        B(k, "complete-variable", "M-$");
        B(k, "complete-command", "M-!");
        B(k, "sabbrev-expand", "M-'");
        B(k, "complete-glob", "M-g");
        B(k, "complete-dynamic-history", "M-C-i", "M-TAB");
        for (int d = 0; d <= 9; d++) B(k, "digit-argument", "M-" + d);
    }

    // Not in ble.sh: Windows clipboard paste, switching editing modes, and S-RET accepting the
    // suggestion when one is shown (ble.sh's auto_complete keymap) / inserting a newline otherwise.
    static void BindBlePs1Extras(Keymap k)
    {
        B(k, "accept-suggestion-or-newline", "S-RET");
        B(k, "paste-from-clipboard", "C-S-v");
        B(k, k.Name == "emacs" ? "vi-editing-mode" : "emacs-editing-mode", "C-M-j");
    }
}
