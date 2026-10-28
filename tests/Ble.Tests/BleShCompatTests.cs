using System.Management.Automation;
using Ble;
using Xunit;

namespace Ble.Tests;

/// <summary>Tests for the TODO items: unclosed-pair highlighting, ambiguous suggestions, the list view,
/// ble.sh options/faces/.blerc, and history reset.</summary>
public class BleShCompatTests
{
    static string?[] Faces(string text)
    {
        Harness.ResetGlobals();
        return new Highlighter(new FakeShell()).Highlight(text);
    }

    [Fact]
    public void UnclosedPairMarksTheLastCharacter()
    {
        // ble.sh: the opener and the last character are syntax_error while the nest is open
        var f = Faces("echo 'abc");
        Assert.Equal("syntax_error", f[5]);
        Assert.Equal("syntax_quoted", f[6]);
        Assert.Equal("syntax_error", f[^1]);
        f = Faces("echo (1 + 2");
        Assert.Equal("syntax_error", f[5]);
        Assert.Equal("syntax_error", f[^1]);
        // closed: nothing marked
        Assert.DoesNotContain("syntax_error", Faces("echo (1 + 2)"));
        Assert.DoesNotContain("syntax_error", Faces("echo 'abc'"));
        // an incomplete pipeline is not a nest
        Assert.NotEqual("syntax_error", Faces("Get-ChildItem |")[^1]);
    }

    static void WildcardCompleter(Harness h, params string[] candidates)
    {
        h.S.Completer = (text, cursor) =>
        {
            int s = cursor;
            while (s > 0 && !char.IsWhiteSpace(text[s - 1])) s--;
            var w = text.Substring(s, cursor - s).Replace('/', '\\');
            var pat = new WildcardPattern(w.Contains('*') ? w : w + "*", WildcardOptions.IgnoreCase);
            var items = candidates.Where(c => pat.IsMatch(c))
                .Select(c => new CompletionItem(c, c, c, CompletionResultType.ProviderContainer)).ToList();
            return new CompletionSet(s, cursor - s, items);
        };
    }

    static void Idle(Harness h)
    {
        Options.Set("complete_auto_completion", "1");
        Options.Set("complete_auto_delay", "0");
        h.E.UpdateSuggestion();
        h.E.GetType().GetMethod("RunIdle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(h.E, null);
    }

    [Fact]
    public void AmbiguousSuggestionShowsBracketsAndReplacesTheWord()
    {
        var h = new Harness();
        WildcardCompleter(h, @".local\bin", @".local\lib");
        Options.Set("complete_auto_completion", "1");
        h.Type("ls .local/bn");
        Idle(h);
        Assert.Equal(" [.local/bin] ", h.E.Suggestion);
        h.Keys("S-RET");
        Assert.Equal("ls .local/bin|", h.State);
    }

    [Fact]
    public void PathSuggestionKeepsTheUsersSeparator()
    {
        var h = new Harness();
        WildcardCompleter(h, @".local\bin");
        Options.Set("complete_auto_completion", "1");
        h.Type("ls .local/b");
        Idle(h);
        Assert.Equal("in", h.E.Suggestion);
        h.Keys("right");
        Assert.Equal("ls .local/bin|", h.State);
    }

    [Fact]
    public void AmbiguousSuggestionAcceptsWordByWord()
    {
        var h = new Harness();
        WildcardCompleter(h, "hello-world");
        Options.Set("complete_auto_completion", "1");
        h.Type("x hlw");
        Idle(h);
        Assert.Equal(" [hello-world] ", h.E.Suggestion);
        h.Keys("M-f");
        Assert.Equal("x hello|", h.State);
        Assert.Equal("-world", h.E.Suggestion);
    }

    [Fact]
    public void ListViewSelectsHistoryEntries()
    {
        var h = new Harness("git status", "git commit", "echo git", "ls");
        Options.Set("complete_auto_view", "list");
        h.Type("git");
        Assert.Null(h.E.Suggestion);
        Assert.Equal(new[] { "git commit", "git status", "echo git" }, h.E.ListViewItems);
        var panels = h.E.BuildModel().Panels.Select(p => p.PlainText).ToList();
        Assert.Contains(panels, p => p.Contains("git commit") && p.Contains("[History]"));
        h.Keys("down");
        Assert.Equal("git commit|", h.State);
        h.Keys("down", "down");
        Assert.Equal("echo git|", h.State);
        h.Keys("down");                       // past the end: back to the typed text
        Assert.Equal("git|", h.State);
        h.Keys("up");
        Assert.Equal("echo git|", h.State);
        h.Keys("C-g");                        // cancel: typed text, list hidden
        Assert.Equal("git|", h.State);
        Assert.Null(h.E.ListViewItems);
        h.Type(" s");
        Assert.Equal(new[] { "git status" }, h.E.ListViewItems);
    }

    [Fact]
    public void ListViewShiftEnterTakesTheFirstOrSelectedEntry()
    {
        var h = new Harness("git status", "git status -s", "git log");
        Options.Set("complete_auto_view", "list");
        h.Type("git s");
        Assert.Equal(new[] { "git status -s", "git status" }, h.E.ListViewItems);
        h.Keys("S-RET");                      // first entry
        Assert.Equal("git status -s|", h.State);
        Assert.Null(h.E.ListViewItems);       // nothing longer starts with it
        h.Keys("C-u");
        h.Type("git s");
        h.Keys("down", "down", "S-RET");      // the selected entry
        Assert.Equal("git status|", h.State);
        Assert.Equal(new[] { "git status -s" }, h.E.ListViewItems);   // and the list goes on
        h.Keys("S-RET");
        Assert.Equal("git status -s|", h.State);
    }

    [Fact]
    public void ListViewHistoryKeysMoveThroughTheList()
    {
        var h = new Harness("echo a1", "echo a2", "echo a3");
        Options.Set("complete_auto_view", "list");
        Keymaps.Get("emacs").Bind("M-j", "history-search-backward");
        Keymaps.Get("emacs").Bind("M-k", "history-search-forward");
        h.Type("echo a");
        h.Keys("M-j", "M-j");                 // older = down
        Assert.Equal("echo a2|", h.State);
        Assert.Equal(3, h.E.ListViewItems!.Count);   // the list stays
        Assert.Equal(1, h.E.ListViewSelected);
        h.Keys("M-k");                        // newer = up
        Assert.Equal("echo a3|", h.State);
        Assert.Contains(h.E.BuildModel().Panels, p => p.PlainText.StartsWith("> echo a3"));
    }

    [Fact]
    public void ListViewMaxLinesAndMultiline()
    {
        var h = new Harness("echo 1", "echo 2", "echo 3", "Write-Output x");
        Options.Set("complete_auto_view", "list");
        Options.Set("complete_auto_view_maxlines", "2");
        h.Type("echo");
        Assert.Equal(2, h.E.ListViewItems!.Count);
        h.Keys("C-u");
        h.Type("if ($x) {");
        h.Keys("M-RET");
        h.Type("Write");
        Assert.Equal(new[] { "Write-Output x" }, h.E.ListViewItems);   // the current line is the query
        h.Keys("S-RET");
        Assert.Equal("if ($x) {\nWrite-Output x|", h.State);
        Options.Set("complete_auto_view_multiline", "");
        h.Keys("DEL");
        Assert.Null(h.E.ListViewItems);
    }

    [Fact]
    public void ListViewAddsCompletionsWhenIdle()
    {
        var h = new Harness("ls .local/share");
        WildcardCompleter(h, @".local\bin", @".local\lib");
        Options.Set("complete_auto_view", "list");
        h.Type("ls .local/");
        Idle(h);
        var entries = h.E.ListViewEntries!;
        Assert.Equal(("ls .local/share", "History"), (entries[0].Line, entries[0].Source));
        Assert.Contains(entries, e => e.Line == "ls .local/bin" && e.Source == "File" && e.Display == ".local/bin");
        // only the completed word is shown, not "ls " again
        Assert.Contains(h.E.BuildModel().Panels, p => p.PlainText.StartsWith("  .local/bin ") && p.PlainText.EndsWith("[File]"));
        Assert.Contains(entries, e => e.Line == "ls .local/lib");
        Options.Set("complete_auto_view_source", "history");
        h.Type("l");
        Idle(h);
        Assert.Null(h.E.ListViewItems);
    }

    [Fact]
    public void ListViewShowsArgumentValuesAfterASpace()
    {
        var h = new Harness();
        h.S.Completer = (text, cursor) => new CompletionSet(cursor, 0, new()
        {
            new CompletionItem("Stop", "Stop", "Stop", CompletionResultType.ParameterValue),
            new CompletionItem("Continue", "Continue", "Continue", CompletionResultType.ParameterValue),
            new CompletionItem("file.txt", "file.txt", "file.txt", CompletionResultType.ProviderItem),
        });
        Options.Set("complete_auto_view", "list");
        h.Type("gci -ErrorAction ");
        Idle(h);
        Assert.Equal(new[] { "gci -ErrorAction Stop", "gci -ErrorAction Continue" }, h.E.ListViewItems);   // no files after a space
        Options.Set("complete_auto_view_completion", "line");
        h.Keys("DEL"); h.Type(" ");
        Idle(h);
        Assert.Contains(h.E.BuildModel().Panels, p => p.PlainText.StartsWith("  gci -ErrorAction Stop"));
    }

    [Fact]
    public void ListViewSelectionHighlightIsConfigurable()
    {
        var h = new Harness("echo abc");
        Options.Set("complete_auto_view", "list");
        Ble.Faces.Set("list_view_selected", "fg=green,bold");
        var sgr = Ble.Faces.Sgr("list_view_selected");
        h.Type("echo");
        h.Keys("down");
        PanelLine Row() => h.E.BuildModel().Panels.First(p => p.PlainText.StartsWith("> "));
        // line (default): marker, text, padding and source all get the face
        Assert.All(Row().Segments.Where(s => s.Text.Length > 0), s => Assert.Contains(sgr, s.Sgr));
        // entry: only the entry's text; the marker stays but is not colored
        Options.Set("complete_auto_view_selected", "entry");
        var row = Row();
        Assert.Equal(("> ", ""), row.Segments[0]);
        Assert.Contains(row.Segments, s => s.Text == " abc" && s.Sgr == sgr);
        Assert.DoesNotContain(sgr, row.Segments.Last().Sgr);   // [History]
        // color: only the text color changes, with its own face (no background by default)
        Options.Set("complete_auto_view_selected", "color");
        var colorSgr = Ble.Faces.Sgr("list_view_selected_color");
        Assert.Equal("\x1b[38;5;33;1m", colorSgr);
        row = Row();
        Assert.Equal(("> ", ""), row.Segments[0]);
        Assert.Contains(row.Segments, s => s.Text == " abc" && s.Sgr == colorSgr);
    }

    [Fact]
    public void ListViewStillShowsTheBracketCorrection()
    {
        var h = new Harness();
        WildcardCompleter(h, @".local\bin");
        Options.Set("complete_auto_view", "list");
        Options.Set("complete_auto_completion", "1");   // the default; the harness turns it off
        h.Type("ls .local/bn");
        Idle(h);
        Assert.Equal(" [.local/bin] ", h.E.Suggestion);
        h.Keys("S-RET");                        // no list entries: S-RET takes the correction
        Assert.Equal("ls .local/bin|", h.State);
        // a plain prefix completion is not shown inline in list mode (the list has it)
        h.Keys("C-u");
        h.Type("ls .local/b");
        Idle(h);
        Assert.Null(h.E.Suggestion);
        Options.Set("complete_auto_view_ambiguous", "");
        h.Keys("C-u");
        h.Type("ls .local/bn");
        Idle(h);
        Assert.Null(h.E.Suggestion);
    }

    [Fact]
    public void F2TogglesTheListView()
    {
        var h = new Harness("git status");
        h.Type("gi");
        Assert.Equal("t status", h.E.Suggestion);
        h.Keys("f2");
        Assert.Equal("list", Options.Get("complete_auto_view"));
        Assert.Null(h.E.Suggestion);
        Assert.NotNull(h.E.ListViewItems);
        h.Keys("f2");
        Assert.Equal("t status", h.E.Suggestion);
    }

    [Fact]
    public void BleShOptionsExistWithAliasesAndListOperators()
    {
        Harness.ResetGlobals();
        Assert.True(Options.Exists("vbell_duration"));
        Assert.True(Options.Exists("term_true_colors"));
        Options.Apply("rps1=x");                                  // obsolete name
        Assert.Equal("x", Options.Get("prompt_rps1"));
        Options.Apply("bleopt_prompt_ps2=> ");                     // bleopt_ prefix
        Assert.Equal("> ", Options.Get("prompt_ps2"));
        Options.Apply("complete_auto_complete_opts+=history-disabled");
        Options.Apply("complete_auto_complete_opts+=foo");
        Assert.Equal("history-disabled:foo", Options.Get("complete_auto_complete_opts"));
        Options.Apply("complete_auto_complete_opts-=history-disabled");
        Assert.Equal("foo", Options.Get("complete_auto_complete_opts"));
        Options.Apply("my_option:=1");                            // define
        Assert.Equal("1", Options.Get("my_option"));
        Options.Apply("keymap_vi_*_cursor=3");                    // wildcard
        Assert.Equal("3", Options.Get("keymap_vi_nmap_cursor"));
        Options.Set("default_keymap", "auto");
        Assert.Equal("emacs", Options.Get("default_keymap"));
        Assert.Throws<ArgumentException>(() => Options.Apply("no_such=1"));
        Harness.ResetGlobals();
        Assert.False(Options.Exists("my_option"));
    }

    [Fact]
    public void BleShFaceSpecs()
    {
        Harness.ResetGlobals();
        Assert.Equal("\x1b[1;7m", Ble.Faces.ToSgr("standout"));
        Assert.Equal("\x1b[32m", Ble.Faces.ToSgr("bold,none,fg=green"));
        Assert.Equal("\x1b[38;2;255;0;0m", Ble.Faces.ToSgr("fg=rgb:255/0/0"));
        Assert.Equal("\x1b[38;2;255;0;0m", Ble.Faces.ToSgr("fg=hsl:0/100%/50%"));
        Assert.Equal("\x1b[1;31m", Ble.Faces.ToSgr("sgrspec:1;31"));
        Ble.Faces.Apply("region=copy:syntax_error");
        Assert.Equal(Ble.Faces.GetSpec("syntax_error"), Ble.Faces.GetSpec("region"));
        Ble.Faces.Apply("my_face:=bold");
        Assert.Equal("bold", Ble.Faces.GetSpec("my_face"));
        Assert.Throws<ArgumentException>(() => Ble.Faces.Apply("no_face=bold"));
        Ble.Faces.ResetFace("region");
        Assert.Equal(Ble.Faces.Defaults["region"], Ble.Faces.GetSpec("region"));
    }

    [Fact]
    public void BleRcAppliesBleShConfiguration()
    {
        Harness.ResetGlobals();
        var rc = new BleRc();
        rc.Run("""
            # comment
            bleopt complete_auto_delay=100 prompt_rps1='\w'
            bleopt exec_errexit_mark=$'\e[91m[exit %d]\e[m'
            ble-face -s region fg=231,bg=60
            ble-face auto_complete='fg=240,underline,italic'
            ble-sabbrev L='| less' gst='git status'
            dir=$HOME/x
            bleopt prompt_ps1_final="$dir"

            function blerc/vim-load-hook {
              bleopt keymap_vi_mode_show=
              ble-bind -m vi_nmap --cursor 2
              ble-bind -m vi_imap --cursor 5
              ble-bind -f 'C-x C-r' 'redo'
              ble-bind -m vi_xmap -s 'Y' '" + y'
              ble-bind -m vi_imap -x 'C-t' 'echo hi'
            }
            blehook/eval-after-load keymap_vi blerc/vim-load-hook
            blehook/eval-after-load keymap_emacs later_hook
            later_hook() { ble-bind -f 'C-x t' kill-line; }

            if [[ $TERM == xterm ]]; then
              bleopt complete_auto_delay=1
            fi
            [[ -f x ]] && bleopt prompt_ps2='no'
            set -o vi
            echo done
            """);
        Assert.Equal("100", Options.Get("complete_auto_delay"));
        Assert.Equal(@"\w", Options.Get("prompt_rps1"));
        Assert.Equal("\x1b[91m[exit %d]\x1b[m", Options.Get("exec_errexit_mark"));
        Assert.Equal("fg=231,bg=60", Ble.Faces.GetSpec("region"));
        Assert.Equal("fg=240,underline,italic", Ble.Faces.GetSpec("auto_complete"));
        Assert.Equal("| less", Editor.Abbrevs["L"]);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/x", Options.Get("prompt_ps1_final"));
        Assert.Equal("", Options.Get("keymap_vi_mode_show"));
        Assert.Equal("2", Options.Get("keymap_vi_nmap_cursor"));
        Assert.True(Keymaps.Get("vi_imap").TryGet("C-x C-r", out var b) && b.Widget == "redo");   // hook keymap = vi_imap
        Assert.True(Keymaps.Get("vi_xmap").TryGet("Y", out var y) && y.Kind == BindingKind.Macro);
        Assert.True(Keymaps.Get("emacs").TryGet("C-x t", out var t) && t.Widget == "kill-line"); // deferred hook
        Assert.Equal(">> ", Options.Get("prompt_ps2"));          // && after a bash test is skipped
        Assert.Equal("vi", Options.Get("default_keymap"));
        Assert.Contains(rc.Warnings, w => w.Contains("-x") && w.Contains("C-t"));
        Assert.Contains(rc.Warnings, w => w.Contains("'if' block"));
        Assert.Contains(rc.Warnings, w => w.Contains("'echo'"));
    }

    [Fact]
    public void BleRcReportsUnknownWidgetsAndKeymaps()
    {
        Harness.ResetGlobals();
        var rc = new BleRc();
        rc.Run("ble-bind -f C-a no-such-widget\nble-bind -m vi_omap -f 'g g' vi-command/first-nol\nx=$(date)");
        Assert.Equal(3, rc.Warnings.Count);
        Assert.Contains(rc.Warnings, w => w.StartsWith("line 1:") && w.Contains("no-such-widget"));
        Assert.Contains(rc.Warnings, w => w.Contains("vi_omap"));
        Assert.Contains(rc.Warnings, w => w.Contains("$(...)"));
    }

    [Fact]
    public void BleRcUnbindInMissingKeymapIsSilentAndBashWidgetsAreExplained()
    {
        Harness.ResetGlobals();
        var rc = new BleRc();
        rc.Run("""
            function ble/widget/blerc/copy {
              ble/widget/vi-command/operator y || return
            }
            ble-bind -m vi_omap -f Y -
            ble-bind -m vi_xmap -f 'Y' blerc/copy
            """);
        var w = Assert.Single(rc.Warnings);
        Assert.Contains("'blerc/copy' is a bash function", w);
    }

    [Fact]
    public void ViModeNamesComeFromOptions()
    {
        var h = new Harness().Vi();
        Options.Set("keymap_vi_mode_name_insert", "EINFÜGEN");
        Assert.Contains(h.E.BuildModel().Panels, p => p.PlainText == "-- EINFÜGEN --");
        h.Keys("ESC");
        Assert.Contains(h.E.BuildModel().Panels, p => TextWidth.StripAnsi(p.PlainText) == "~");   // keymap_vi_mode_string_nmap
        h.Type("V");
        Assert.Contains(h.E.BuildModel().Panels, p => p.PlainText == "-- VISUAL LINE --");
    }

    [Fact]
    public void EmacsShowsMultilineModeLine()
    {
        var h = new Harness();
        h.Type("a");
        Assert.DoesNotContain(h.E.BuildModel().Panels, p => p.PlainText.Contains("MULTILINE"));
        h.Keys("M-RET");
        Assert.Contains(h.E.BuildModel().Panels, p => TextWidth.StripAnsi(p.PlainText).StartsWith("-- MULTILINE -- (RET or C-m: insert a newline, C-j: run)"));
    }

    [Fact]
    public void ElapsedOptions()
    {
        Harness.ResetGlobals();
        Assert.Equal(10000, Session.ElapsedThreshold());
        Options.Set("exec_elapsed_enabled", "real>=500");
        Assert.Equal(500, Session.ElapsedThreshold());
        Options.Set("exec_elapsed_enabled", "");
        Assert.Equal(-1, Session.ElapsedThreshold());
        Assert.Equal("elapsed 1.0s (CPU -%)", Session.FormatElapsedMark("elapsed %s (CPU %s%%)", "1.0s"));
    }

    [Fact]
    public void HistoryRemoveAndRewrite()
    {
        Harness.ResetGlobals();
        var file = Path.Combine(Path.GetTempPath(), "ble-hist-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(file, "a\nb\nc\n");
            var h = new History();
            h.Load(file);
            h.RemoveRange(1, 1);
            h.Rewrite();
            Assert.Equal(new[] { "a", "c" }, History.Parse(File.ReadAllText(file)));
            h.Clear();
            h.Rewrite();
            Assert.Equal("", File.ReadAllText(file));
            h.Add("d");
            Assert.Equal(new[] { "d" }, History.Parse(File.ReadAllText(file)));
        }
        finally { File.Delete(file); }
    }
}
