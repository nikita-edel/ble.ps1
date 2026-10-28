using System.ComponentModel;
using System.Diagnostics;
using System.Management.Automation;
using System.Text;

namespace Ble;

// Optional fzf integration (ble-import integration/fzf-key-bindings, integration/fzf-completion).
// fzf is only needed when these widgets are used; without it they show a message.
public sealed partial class Editor
{
    /// <summary>Test hook: replaces running the fzf executable (args, stdin lines) -> selected lines or null.</summary>
    public Func<string, IReadOnlyList<string>?, List<string>?>? FzfRunner;

    static string FzfExecutable => Environment.GetEnvironmentVariable("BLE_FZF") is { Length: > 0 } p ? p : "fzf";

    /// <summary>Runs fzf below the command line. Returns the selected entries, or null if cancelled/unavailable.</summary>
    List<string>? RunFzf(string args, IReadOnlyList<string>? input)
    {
        if (FzfRunner != null) return FzfRunner(args, input);
        var opts = "--height=40% --layout=reverse " + args;
        bool prev = Term.TreatControlCAsInput;
        Renderer.EraseDrawing();
        Term.TreatControlCAsInput = false;
        try
        {
            var psi = new ProcessStartInfo(FzfExecutable, opts)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = input != null,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Shell.CurrentDirectory,
            };
            if (input != null) psi.StandardInputEncoding = new UTF8Encoding(false);
            using var p = Process.Start(psi)!;
            Task? feeder = null;
            if (input != null)
            {
                feeder = Task.Run(() =>
                {
                    try
                    {
                        foreach (var line in input) { p.StandardInput.Write(line); p.StandardInput.Write('\0'); }
                        p.StandardInput.Close();
                    }
                    catch (IOException) { }
                });
            }
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            feeder?.Wait(1000);
            if (p.ExitCode != 0) return null;
            var sep = output.Contains('\0') ? '\0' : '\n';
            return output.Split(sep).Select(s => s.TrimEnd('\r')).Where(s => s.Length > 0).ToList();
        }
        catch (Win32Exception)
        {
            Info = "ble: fzf not found (install it, e.g. `winget install junegunn.fzf`, or set $env:BLE_FZF)";
            InfoFace = "status_error";
            return null;
        }
        finally
        {
            Term.TreatControlCAsInput = prev;
            Renderer.Begin(Term.CursorLeft);
        }
    }

    static string QuoteArgument(string s)
    {
        if (s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "._-/\\:~+@,".Contains(c))) return s;
        return "'" + s.Replace("'", "''") + "'";
    }

    static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";

    static void RegisterFzfWidgets()
    {
        // C-t: insert files / directories
        W("fzf-file-widget", (e, k) =>
        {
            var sel = e.RunFzf("--multi --scheme=path " + Env("FZF_CTRL_T_OPTS"), null);
            if (sel == null || sel.Count == 0) return;
            var ins = string.Join(' ', sel.Select(QuoteArgument));
            if (e.Point > 0 && !char.IsWhiteSpace(e.Text[e.Point - 1])) ins = " " + ins;
            e.Insert(ins + " ");
        });
        // M-c: change directory
        W("fzf-cd-widget", (e, k) =>
        {
            var sel = e.RunFzf("--walker=dir,follow,hidden --scheme=path " + Env("FZF_ALT_C_OPTS"), null);
            if (sel == null || sel.Count == 0) return;
            var dir = sel[0];
            e.RunAboveLine(() => e.Shell.RunCommand(ScriptBlock.Create("Set-Location -LiteralPath '" + dir.Replace("'", "''") + "'")));
        });
        // C-r: history
        W("fzf-history-widget", (e, k) =>
        {
            var items = Enumerable.Range(0, e.History.Count).Select(i => e.History[e.History.Count - 1 - i]).ToList();
            var q = e.Text.Replace("\"", "\\\"");
            var sel = e.RunFzf($"--read0 --print0 --scheme=history --no-multi --query=\"{q}\" " + Env("FZF_CTRL_R_OPTS"), items);
            if (sel == null || sel.Count == 0) return;
            e.SetText(sel[0], sel[0].Length);
        });
    }

    /// <summary>fzf-completion: a word ending with the trigger (e.g. '**') opens fzf. Returns true if handled.</summary>
    bool TryFzfCompletion()
    {
        var trig = Options.Get("complete_fzf_trigger");
        if (trig.Length == 0) return false;
        int start = Point;
        while (start > 0 && !char.IsWhiteSpace(Text[start - 1]) && "|;(){}".IndexOf(Text[start - 1]) < 0) start--;
        var word = Text.Substring(start, Point - start);
        if (!word.EndsWith(trig, StringComparison.Ordinal)) return false;
        var query = word.Substring(0, word.Length - trig.Length);
        var sel = RunFzf("--multi --scheme=path --query=\"" + query.Replace("\"", "\\\"") + "\" " + Env("FZF_COMPLETION_OPTS"), null);
        if (sel == null || sel.Count == 0) return true;
        var rep = string.Join(' ', sel.Select(QuoteArgument)) + " ";
        Text = Text.Substring(0, start) + rep + Text.Substring(Point);
        Point = start + rep.Length;
        return true;
    }
}
