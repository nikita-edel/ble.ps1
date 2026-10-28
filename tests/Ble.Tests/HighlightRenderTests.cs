using Ble;
using Xunit;

namespace Ble.Tests;

public class HighlightTests
{
    static string[] Faces(string text)
    {
        Harness.ResetGlobals();
        var hl = new Highlighter(new FakeShell());
        return hl.Highlight(text).Select(f => f ?? "").ToArray();
    }

    static string FaceAt(string text, string sub, int offset = 0) => Faces(text)[text.IndexOf(sub, StringComparison.Ordinal) + offset];

    [Fact]
    public void CommandKinds()
    {
        Assert.Equal("command_builtin", FaceAt("Get-ChildItem -Path x", "Get-ChildItem"));
        Assert.Equal("argument_option", FaceAt("Get-ChildItem -Path x", "-Path"));
        Assert.Equal("command_alias", FaceAt("ls", "ls"));
        Assert.Equal("command_file", FaceAt("git status", "git"));
        Assert.Equal("syntax_error", FaceAt("nosuchcmd arg", "nosuchcmd"));
        Assert.Equal("command_builtin", FaceAt("ls | Where-Object { $_ }", "Where-Object"));
        Assert.Equal("syntax_delimiter", FaceAt("ls | Where-Object", "|"));
    }

    [Fact]
    public void StringsVariablesNumbersComments()
    {
        var t = "echo 'lit' \"a $x `n\" 42 # note";
        Assert.Equal("syntax_quotation", FaceAt(t, "'lit'"));
        Assert.Equal("syntax_quoted", FaceAt(t, "'lit'", 1));
        Assert.Equal("syntax_param_expansion", FaceAt(t, "$x"));
        Assert.Equal("syntax_escape", FaceAt(t, "`n"));
        Assert.Equal("syntax_number", FaceAt(t, "42"));
        Assert.Equal("syntax_comment", FaceAt(t, "# note"));
    }

    [Fact]
    public void VariablesFollowBleShRules()
    {
        // '$' is syntax_param_expansion, the name is colored by the variable's state
        Assert.Equal("syntax_param_expansion", FaceAt("echo $x", "$x"));
        Assert.Equal("syntax_varname", FaceAt("echo $x", "$x", 1));
        Assert.Equal("varname_unset", FaceAt("echo $undefinedVar", "$undefinedVar", 1));
        Assert.Equal("varname_export", FaceAt("echo $env:PATH", "PATH"));
        Assert.Equal("syntax_param_expansion", FaceAt("echo $env:PATH", "env:"));
        Assert.Equal("syntax_param_expansion", FaceAt("echo ${x}", "{"));
        Assert.Equal("syntax_varname", FaceAt("echo $_", "$_", 1));
        // assignment to an unset variable: varname_new; to an existing one: its state
        Assert.Equal("varname_new", FaceAt("$newVar = 1", "newVar"));
        Assert.Equal("syntax_varname", FaceAt("$x = 1", "x"));
    }

    [Fact]
    public void VariableKinds()
    {
        Harness.ResetGlobals();
        var sh = new FakeShell();
        sh.VariableKinds["arr"] = VarKind.Array;
        sh.VariableKinds["h"] = VarKind.Hash;
        sh.VariableKinds["ro"] = VarKind.Readonly;
        sh.VariableKinds["n"] = VarKind.Number;
        sh.VariableKinds["empty"] = VarKind.Empty;
        var t = "echo $arr $h $ro $n $empty";
        var f = new Highlighter(sh).Highlight(t);
        Assert.Equal("varname_array", f[t.IndexOf("arr")]);
        Assert.Equal("varname_hash", f[t.IndexOf("$h") + 1]);
        Assert.Equal("varname_readonly", f[t.IndexOf("ro")]);
        Assert.Equal("varname_number", f[t.IndexOf("$n") + 1]);
        Assert.Equal("varname_empty", f[t.IndexOf("empty")]);
    }

    [Fact]
    public void DefinitionsDocumentsDotSourceGlobTilde()
    {
        Assert.Equal("syntax_function_name", FaceAt("function Get-Foo { }", "Get-Foo"));
        Assert.Equal("syntax_document_begin", FaceAt("@'\nhi\n'@", "@'"));
        Assert.Equal("syntax_document", FaceAt("@'\nhi\n'@", "hi"));
        Assert.Equal("command_builtin_dot", FaceAt(". ./x.ps1", "."));
        Assert.NotEqual("command_builtin_dot", FaceAt("$a.b", "."));
        Assert.Equal("syntax_glob", FaceAt("ls *.txt", "*"));
        Assert.Equal("syntax_tilde", FaceAt("ls ~/x", "~"));
    }

    [Fact]
    public void KeywordsAndTypes()
    {
        Assert.Equal("command_keyword", FaceAt("if ($true) { }", "if"));
        Assert.Equal("syntax_type", FaceAt("[int]5", "int"));
    }

    [Fact]
    public void ParseErrorsAndUnclosedInput()
    {
        Assert.Contains("syntax_error", Faces("echo (1 + )"));
        // unclosed string: the opening quote is marked
        Assert.Equal("syntax_error", FaceAt("echo 'abc", "'"));
        Assert.Equal("syntax_quoted", FaceAt("echo 'abc", "abc"));
        // unclosed brackets: the opener is marked
        Assert.Equal("syntax_error", FaceAt("if ($x) { echo", "{"));
        Assert.Equal("syntax_error", FaceAt("echo (1 + 2", "("));
        Assert.NotEqual("syntax_error", FaceAt("echo (1 + 2)", "("));
        // can be turned off
        Options.Set("highlight_incomplete", "");
        var hl = new Highlighter(new FakeShell());
        Assert.DoesNotContain("syntax_error", hl.Highlight("echo 'abc"));
    }

    [Fact]
    public void UnknownParameters()
    {
        Assert.Equal("argument_error", FaceAt("Get-ChildItem -Nope", "-Nope"));
        Assert.Equal("argument_option", FaceAt("Get-ChildItem -Rec", "-Rec"));      // unique prefix
        Assert.Equal("argument_option", FaceAt("Get-ChildItem -ea 0", "-ea"));      // alias
        Assert.Equal("argument_option", FaceAt("git -x", "-x"));                    // native: not checked
        Assert.Equal("argument_option", FaceAt("Get-ChildItem @p -Nope", "-Nope")); // splatting: not checked
    }

    [Fact]
    public void FileNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ble hl " + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "");
        try
        {
            Harness.ResetGlobals();
            var sh = new FakeShell { Cwd = dir };
            var hl = new Highlighter(sh);
            var t = @"Get-Content f.txt missing.txt .\nope\x.txt origin/main '" + dir + @"\nothere'";
            var faces = hl.Highlight(t);
            Assert.Equal("filename_other", faces[t.IndexOf("f.txt")]);
            Assert.Null(faces[t.IndexOf("missing")]);                       // not path-like
            Assert.Equal("filename_warning", faces[t.IndexOf(@".\nope")]);   // path-like and missing
            Assert.Null(faces[t.IndexOf("origin/main")]);                   // first segment does not exist
            Assert.Equal("filename_warning", faces[t.IndexOf("nothere")]);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class RenderTests
{
    static VtScreen Draw(Harness h, int width = 40)
    {
        h.T.Width = width;
        h.E.Renderer.Begin(4);
        var scr = new VtScreen(width);
        scr.Feed("PS> ");
        h.T.Output.Clear();
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        return scr;
    }

    [Fact]
    public void DrawsTextAfterPromptAndPlacesCursor()
    {
        var h = new Harness().Type("echo hi");
        h.Keys("C-b");
        var scr = Draw(h);
        Assert.Equal("PS> echo hi", scr.Line(0));
        Assert.Equal(0, scr.Row);
        Assert.Equal(4 + 6, scr.Col);
    }

    [Fact]
    public void WrapsLongLinesAndRedrawsInPlace()
    {
        var h = new Harness();
        h.T.Width = 20;
        h.E.Renderer.Begin(4);
        var scr = new VtScreen(20);
        scr.Feed("PS> ");
        h.Type("0123456789abcdefghijKLMNOP");
        h.T.Output.Clear();
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        Assert.Equal("PS> 0123456789abcdef", scr.Line(0));
        Assert.Equal("ghijKLMNOP", scr.Line(1));
        Assert.Equal((1, 10), (scr.Row, scr.Col));
        // shrink the text: second render must erase the old content
        h.Keys("C-u");
        h.Type("x");
        h.T.Output.Clear();
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        Assert.Equal("PS> x", scr.Line(0));
        Assert.Equal("", scr.Line(1));
        Assert.Equal((0, 5), (scr.Row, scr.Col));
    }

    [Fact]
    public void ExactlyFullRowPutsCursorOnNextRow()
    {
        var h = new Harness();
        h.T.Width = 10;
        h.E.Renderer.Begin(4);
        var scr = new VtScreen(10);
        scr.Feed("PS> ");
        h.Type("abcdef");
        h.T.Output.Clear();
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        Assert.Equal("PS> abcdef", scr.Line(0));
        Assert.Equal((1, 0), (scr.Row, scr.Col));
    }

    [Fact]
    public void WideCharactersWrapAsAWhole()
    {
        var h = new Harness();
        h.T.Width = 10;
        h.E.Renderer.Begin(4);
        var scr = new VtScreen(10);
        scr.Feed("PS> ");
        h.Type("abcde日本");
        h.T.Output.Clear();
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        Assert.Equal("PS> abcde", scr.Line(0));
        Assert.Equal("日本", scr.Line(1));
        Assert.Equal((1, 4), (scr.Row, scr.Col));
    }

    [Fact]
    public void MultilineUsesContinuationPrompt()
    {
        var h = new Harness();
        h.Type("if (1) {");
        h.Keys("RET");
        h.Type("x");
        var scr = Draw(h);
        Assert.Equal("PS> if (1) {", scr.Line(0));
        Assert.Equal(">> x", scr.Line(1));
        Assert.Equal((1, 4), (scr.Row, scr.Col));
    }

    [Fact]
    public void SuggestionAndMenuAreDrawnBelow()
    {
        var h = new Harness("echo hello world");
        h.Type("echo h");
        var scr = Draw(h);
        Assert.Equal("PS> echo hello world", scr.Line(0));
        Assert.Equal((0, 10), (scr.Row, scr.Col));

        h = new Harness();
        h.S.CompleteFrom("alpha", "alpine", "alps");
        h.Type("al");
        h.Keys("TAB");
        scr = Draw(h);
        Assert.Equal("PS> alp", scr.Line(0));
        Assert.Contains("alpha", scr.Line(1));
        Assert.Contains("alpine", scr.Line(1));
        Assert.Equal((0, 7), (scr.Row, scr.Col));
    }

    [Fact]
    public void FinalRenderEndsWithNewlineAndNoSuggestion()
    {
        var h = new Harness("echo hello world");
        h.Type("echo h");
        var scr = new VtScreen(40);
        scr.Feed("PS> ");
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        h.T.Output.Clear();
        var m = h.E.BuildModel();
        m.Final = true;
        h.E.Renderer.Render(m);
        scr.Feed(h.T.Output.ToString());
        Assert.Equal("PS> echo h", scr.Line(0));
        Assert.Equal((1, 0), (scr.Row, scr.Col));
    }

    [Fact]
    public void RightPromptShownWhenRoom()
    {
        var h = new Harness();
        Options.Set("prompt_rps1", "[rp]");
        h.E.StartLine();
        h.Type("ls");
        var scr = Draw(h);
        Assert.Equal("PS> ls".PadRight(35) + "[rp]", scr.Line(0));
        Assert.Equal((0, 6), (scr.Row, scr.Col));
    }

    [Fact]
    public void ControlCharactersAreShownAsCaret()
    {
        var h = new Harness();
        h.Keys("C-q", "C-a");
        var scr = Draw(h);
        Assert.Equal("PS> ^A", scr.Line(0));
    }

    [Fact]
    public void ResizeShrinkKeepsDrawingConsistent()
    {
        var h = new Harness();
        h.T.Width = 40;
        h.E.Renderer.Begin(4);
        h.Type("0123456789012345678901234567890");
        h.E.SafeRender();
        // the terminal shrinks to 20 columns: our hard-broken row of 35 cells re-wraps into 2 rows
        h.T.Width = 20;
        h.E.Renderer.AdjustForResize(20);
        Assert.Equal(1, h.E.Renderer.CurRow);
        Assert.Equal(4, h.E.Renderer.StartCol);
    }
}
