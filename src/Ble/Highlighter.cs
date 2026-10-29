using System.Management.Automation;
using System.Management.Automation.Language;

namespace Ble;

static class VariablePathExtensions
{
    /// <summary>Variable name without scope or drive qualifier ($global:x -> x, $env:Path -> Path).</summary>
    public static string Unqualified(this VariablePath vp)
    {
        var p = vp.UserPath;
        int c = p.IndexOf(':');
        return c >= 0 ? p.Substring(c + 1) : p;
    }
}

/// <summary>
/// Computes a face name for every character of the command line, following ble.sh's rules
/// (lib/core-syntax.sh) mapped onto PowerShell syntax:
///   command names       command_builtin (cmdlets) / alias / function / file / keyword / builtin_dot (.) / syntax_error
///   $name, ${name}      '$' and braces: syntax_param_expansion; the name: by the variable's state
///                       (syntax_varname, varname_export/array/hash/readonly/number/empty/unset)
///   name = ...          the assigned name: same, but an unset variable is varname_new
///   'x' "x"             syntax_quoted, quote characters syntax_quotation; here-strings syntax_document(_begin)
///   -Param              argument_option; argument_error when the command has no such parameter
///   paths               filename_* (underlined); filename_warning for paths that do not exist
///   * ? ~               syntax_glob, syntax_tilde
///   errors              syntax_error, including unclosed strings and brackets (highlight_incomplete)
/// </summary>
public sealed class Highlighter
{
    // automatic variables that only exist while a script block / pipeline runs
    static readonly HashSet<string> TransientVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "_", "psitem", "args", "input", "this", "matches", "foreach", "switch", "psboundparameters", "myinvocation",
        "pscmdlet", "event", "eventargs", "eventsubscriber", "sender", "sourceargs", "sourceeventargs", "psscriptroot",
        "pscommandpath", "lastexitcode", "stacktrace", "psdebugcontext",
    };

    readonly IShell _shell;
    readonly Dictionary<string, string> _cmdFace = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, PathKind> _paths = new(StringComparer.Ordinal);
    readonly Dictionary<(string Dir, string Word), bool> _originPaths = new();
    readonly Dictionary<string, VarKind> _vars = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, HashSet<string>?> _params = new(StringComparer.OrdinalIgnoreCase);
    string? _lastText;
    string[]? _lastFaces;

    public Highlighter(IShell shell) { _shell = shell; }

    /// <summary>Forget cached lookups (call once per command line: commands and files may have changed).</summary>
    public void ResetCaches()
    {
        _cmdFace.Clear(); _paths.Clear(); _originPaths.Clear(); _vars.Clear(); _params.Clear();
        _lastText = null; _lastFaces = null;
    }

    public static (Ast Ast, Token[] Tokens, ParseError[] Errors) Parse(string text)
    {
        var ast = Parser.ParseInput(text, out var tokens, out var errors);
        return (ast, tokens, errors);
    }

    public static bool IsIncomplete(string text)
    {
        Parser.ParseInput(text, out _, out var errors);
        return errors.Any(e => e.IncompleteInput);
    }

    public string[] Highlight(string text)
    {
        if (_lastText == text && _lastFaces != null) return _lastFaces;
        var faces = new string[text.Length];
        if (!Options.GetBool("highlight_syntax") || text.Length == 0)
        {
            _lastText = text; _lastFaces = faces;
            return faces;
        }
        var (ast, tokens, errors) = Parse(text);
        Token? prev = null;
        foreach (var t in tokens)
        {
            Apply(t, faces, text, prev);
            if (t.Kind is not (TokenKind.NewLine or TokenKind.Comment)) prev = t;
        }
        if (Options.GetBool("highlight_variable"))
            foreach (var v in AssignmentTargets(ast)) ApplyVariable(v.VariablePath, v.Extent.StartOffset, v.Extent.EndOffset, faces, text, newVar: true);
        CheckParameters(ast, faces);
        bool unclosed = false;
        foreach (var e in errors)
        {
            int s = Math.Clamp(e.Extent.StartOffset, 0, text.Length), en = Math.Clamp(e.Extent.EndOffset, 0, text.Length);
            if (e.IncompleteInput)
            {
                if (!Options.GetBool("highlight_incomplete")) continue;
                // unclosed string or here-string: mark the opening quote
                if (e.ErrorId == "TerminatorExpectedAtEndOfString" && s < text.Length)
                {
                    Fill(faces, s, Math.Min(text.Length, s + (text[s] == '@' ? 2 : 1)), "syntax_error");
                    unclosed = true;
                }
                continue;
            }
            if (en <= s)
            {
                if (s < text.Length) en = s + 1;
                else if (s > 0) { s--; en = s + 1; }
            }
            Fill(faces, s, en, "syntax_error");
        }
        if (Options.GetBool("highlight_incomplete") && errors.Any(e => e.IncompleteInput))
        {
            if (MarkUnclosedBrackets(tokens, faces)) unclosed = true;
            // ble.sh: while a nest (quote, bracket) is still open at the end of the input,
            // the last character is marked as well (core-syntax.sh: _ble_syntax_attr[iN-1]=ATTR_ERR)
            if (unclosed) faces[^1] = "syntax_error";
        }
        _lastText = text; _lastFaces = faces;
        return faces;
    }

    /// <summary>Marks brackets that are never closed. Returns true if there were any.</summary>
    static bool MarkUnclosedBrackets(Token[] tokens, string[] faces)
    {
        var stack = new Stack<Token>();
        void Walk(IEnumerable<Token> ts)
        {
            foreach (var t in ts)
            {
                switch (t.Kind)
                {
                    case TokenKind.LParen: case TokenKind.DollarParen: case TokenKind.AtParen:
                    case TokenKind.LCurly: case TokenKind.AtCurly: case TokenKind.LBracket:
                        stack.Push(t); break;
                    case TokenKind.RParen: case TokenKind.RCurly: case TokenKind.RBracket:
                        if (stack.Count > 0) stack.Pop(); break;
                }
                if (t is StringExpandableToken st && st.NestedTokens != null) Walk(st.NestedTokens);
            }
        }
        Walk(tokens);
        foreach (var t in stack) Fill(faces, t.Extent.StartOffset, t.Extent.EndOffset, "syntax_error");
        return stack.Count > 0;
    }

    static IEnumerable<VariableExpressionAst> AssignmentTargets(Ast ast)
    {
        foreach (var n in ast.FindAll(x => x is AssignmentStatementAst or ForEachStatementAst or ParameterAst, true))
        {
            switch (n)
            {
                case AssignmentStatementAst a:
                    var left = a.Left is ConvertExpressionAst ce ? ce.Child : a.Left;
                    if (left is VariableExpressionAst v) yield return v;
                    break;
                case ForEachStatementAst f:
                    yield return f.Variable;
                    break;
                case ParameterAst p:
                    yield return p.Name;
                    break;
            }
        }
    }

    // argument_error for parameters the command does not have
    void CheckParameters(Ast ast, string[] faces)
    {
        foreach (var ca in ast.FindAll(x => x is CommandAst, true).Cast<CommandAst>())
        {
            var name = ca.GetCommandName();
            if (name == null || ca.InvocationOperator == TokenKind.Dot) continue;
            // splatting or provider paths can bring parameters we cannot see here
            if (ca.CommandElements.Any(el => el is VariableExpressionAst { Splatted: true })) continue;
            // native programs and unknown names have no parameter list (and looking them up is slow)
            if (_cmdFace.TryGetValue(name, out var cf) && cf is "syntax_error" or "command_file") continue;
            if (!_params.TryGetValue(name, out var set))
            {
                var names = _shell.GetParameterNames(name);
                set = names == null ? null : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                _params[name] = set;
            }
            if (set == null) continue;
            foreach (var p in ca.CommandElements.OfType<CommandParameterAst>())
            {
                var pn = p.ParameterName;
                if (pn.Length == 0) continue;
                bool ok = set.Contains(pn) || set.Any(x => x.StartsWith(pn, StringComparison.OrdinalIgnoreCase));
                if (!ok) Fill(faces, p.Extent.StartOffset, Math.Min(p.Extent.EndOffset, p.Extent.StartOffset + 1 + pn.Length), "argument_error");
            }
        }
    }

    static void Fill(string[] faces, int s, int e, string face)
    {
        s = Math.Max(0, s); e = Math.Min(faces.Length, e);
        for (int i = s; i < e; i++) faces[i] = face;
    }

    void Apply(Token t, string[] faces, string text, Token? prev)
    {
        int s = t.Extent.StartOffset, e = t.Extent.EndOffset;
        if (e <= s || s >= text.Length) return;
        var flags = t.TokenFlags;
        switch (t.Kind)
        {
            case TokenKind.Comment:
                Fill(faces, s, e, "syntax_comment"); return;
            case TokenKind.Variable:
            case TokenKind.SplattedVariable:
                ApplyVariable(((VariableToken)t).VariablePath, s, e, faces, text, newVar: false); return;
            case TokenKind.StringLiteral:
                Fill(faces, s, e, "syntax_quoted");
                if ((flags & TokenFlags.CommandName) != 0) ApplyCommandName(((StringToken)t).Value, faces, s, e, quoted: true);
                else ApplyFilename(((StringToken)t).Value, faces, s, e, quoted: true);
                MarkQuotes(text, faces, s, e);
                return;
            case TokenKind.HereStringLiteral:
                Fill(faces, s, e, "syntax_document");
                MarkHereStringDelimiters(faces, s, e);
                return;
            case TokenKind.StringExpandable:
            case TokenKind.HereStringExpandable:
                Fill(faces, s, e, t.Kind == TokenKind.StringExpandable ? "syntax_quoted" : "syntax_document");
                MarkEscapes(text, faces, s, e);
                if (t.Kind == TokenKind.StringExpandable) MarkQuotes(text, faces, s, e);
                else MarkHereStringDelimiters(faces, s, e);
                if (t is StringExpandableToken st && st.NestedTokens != null)
                    foreach (var nt in st.NestedTokens) Apply(nt, faces, text, null);
                return;
            case TokenKind.Number:
                Fill(faces, s, e, "syntax_number"); return;
            case TokenKind.Parameter:
                Fill(faces, s, e, "argument_option"); return;
            case TokenKind.LineContinuation:
                Fill(faces, s, e, "syntax_escape"); return;
            case TokenKind.Label:
                Fill(faces, s, e, "syntax_label"); return;
            case TokenKind.Dot when prev == null || prev.Kind is TokenKind.Pipe or TokenKind.Semi or TokenKind.NewLine or TokenKind.LCurly or TokenKind.AndAnd or TokenKind.OrOr:
                // dot-sourcing, like bash's "." builtin
                Fill(faces, s, e, "command_builtin_dot"); return;
            case TokenKind.Pipe:
            case TokenKind.Semi:
            case TokenKind.AndAnd:
            case TokenKind.OrOr:
            case TokenKind.Ampersand:
            case TokenKind.Redirection:
            case TokenKind.RedirectInStd:
                Fill(faces, s, e, "syntax_delimiter"); return;
            case TokenKind.DollarParen:
            case TokenKind.AtParen:
            case TokenKind.AtCurly:
                Fill(faces, s, e, "syntax_param_expansion"); return;
            case TokenKind.Generic:
            case TokenKind.Identifier:
                // ble.sh: the name in a function definition
                if (prev != null && prev.Kind is TokenKind.Function or TokenKind.Filter or TokenKind.Workflow)
                {
                    Fill(faces, s, e, "syntax_function_name");
                    return;
                }
                if ((flags & TokenFlags.CommandName) != 0)
                {
                    var name = t is StringToken sToken ? sToken.Value : t.Text;
                    ApplyCommandName(name, faces, s, e, quoted: false);
                    MarkEscapes(text, faces, s, e);
                    return;
                }
                if ((flags & TokenFlags.Keyword) != 0) { Fill(faces, s, e, "command_keyword"); return; }
                if ((flags & (TokenFlags.TypeName | TokenFlags.AttributeName)) != 0) { Fill(faces, s, e, "syntax_type"); return; }
                if ((flags & TokenFlags.MemberName) != 0) { Fill(faces, s, e, "syntax_member"); return; }
                if (t.Kind == TokenKind.Generic)
                {
                    var val = t is StringToken gt ? gt.Value : t.Text;
                    ApplyFilename(val, faces, s, e, quoted: false);
                    MarkGlobAndTilde(text, faces, s, e);
                    MarkEscapes(text, faces, s, e);
                }
                return;
        }
        if ((flags & TokenFlags.Keyword) != 0) { Fill(faces, s, e, "command_keyword"); return; }
        if ((flags & (TokenFlags.TypeName | TokenFlags.AttributeName)) != 0) { Fill(faces, s, e, "syntax_type"); return; }
        if ((flags & (TokenFlags.BinaryOperator | TokenFlags.UnaryOperator | TokenFlags.AssignmentOperator)) != 0)
        {
            Fill(faces, s, e, "syntax_expr"); return;
        }
    }

    static bool IsQuote(char c) => c is '\'' or '"' or '‘' or '’' or '“' or '”';

    // ble.sh: syntax_quotation for the quote characters themselves
    static void MarkQuotes(string text, string[] faces, int s, int e)
    {
        if (IsQuote(text[s])) faces[s] = "syntax_quotation";
        if (e - 1 > s && e - 1 < text.Length && IsQuote(text[e - 1])) faces[e - 1] = "syntax_quotation";
    }

    // ble.sh: syntax_document_begin for here-document delimiters
    static void MarkHereStringDelimiters(string[] faces, int s, int e)
    {
        Fill(faces, s, Math.Min(e, s + 2), "syntax_document_begin");
        if (e - s >= 4) Fill(faces, e - 2, e, "syntax_document_begin");
    }

    static void MarkEscapes(string text, string[] faces, int s, int e)
    {
        for (int i = s; i < e - 1; i++)
        {
            if (text[i] == '`') { faces[i] = "syntax_escape"; faces[i + 1] = "syntax_escape"; i++; }
        }
    }

    // ble.sh: syntax_glob for * ? [...] and syntax_tilde for a leading ~
    static void MarkGlobAndTilde(string text, string[] faces, int s, int e)
    {
        if (text[s] == '~' && (s + 1 == e || text[s + 1] is '\\' or '/')) faces[s] = "syntax_tilde";
        for (int i = s; i < e; i++)
        {
            char c = text[i];
            if (c == '`') { i++; continue; }
            if (c is '*' or '?') faces[i] = "syntax_glob";
            else if (c == '[')
            {
                int close = text.IndexOf(']', i + 1, e - i - 1);
                if (close > i + 1) { faces[i] = "syntax_glob"; faces[close] = "syntax_glob"; }
            }
        }
    }

    void ApplyVariable(VariablePath vp, int s, int e, string[] faces, string text, bool newVar)
    {
        int i = s;
        int end = e;
        // sigil ($ or @) and ${ } braces: syntax_param_expansion (ble.sh CTX_PARAM)
        if (i < end && text[i] is '$' or '@') faces[i++] = "syntax_param_expansion";
        if (i < end && text[i] == '{')
        {
            faces[i++] = "syntax_param_expansion";
            if (end - 1 > i && text[end - 1] == '}') faces[--end] = "syntax_param_expansion";
        }
        var up = vp.UserPath;
        int colon = up.IndexOf(':');
        if (colon >= 0 && i + colon + 1 <= end)
        {
            Fill(faces, i, i + colon + 1, "syntax_param_expansion");
            i += colon + 1;
        }
        Fill(faces, i, end, VarFace(vp, newVar));
    }

    string VarFace(VariablePath vp, bool newVar)
    {
        if (!Options.GetBool("highlight_variable")) return "syntax_varname";
        string key;
        if (vp.IsDriveQualified)
        {
            if (!string.Equals(vp.DriveName, "env", StringComparison.OrdinalIgnoreCase)) return "syntax_varname";
            key = "env:" + vp.Unqualified();
        }
        else if (vp.UserPath.StartsWith("using:", StringComparison.OrdinalIgnoreCase) || vp.IsScript || vp.IsPrivate)
            return "syntax_varname";
        else key = vp.Unqualified();
        if (key.Length == 0) return "syntax_varname";
        if (!_vars.TryGetValue(key, out var kind))
        {
            kind = _shell.GetVariableKind(key);
            _vars[key] = kind;
        }
        if (kind == VarKind.Unset && TransientVariables.Contains(key)) return "syntax_varname";
        return kind switch
        {
            VarKind.Unset => newVar ? "varname_new" : "varname_unset",
            VarKind.Export => "varname_export",
            VarKind.Array => "varname_array",
            VarKind.Hash => "varname_hash",
            VarKind.Readonly => "varname_readonly",
            VarKind.Number => "varname_number",
            VarKind.Empty => "varname_empty",
            _ => "syntax_varname",
        };
    }

    void ApplyCommandName(string name, string[] faces, int s, int e, bool quoted)
    {
        var face = CommandFace(name);
        if (quoted && face == "syntax_error") return;
        Fill(faces, s, e, face);
    }

    string CommandFace(string name)
    {
        if (!_cmdFace.TryGetValue(name, out var face))
        {
            var kind = _shell.ResolveCommand(name);
            face = kind switch
            {
                CommandKind.Alias => "command_alias",
                CommandKind.Function => "command_function",
                CommandKind.Cmdlet => "command_builtin",
                CommandKind.Application => "command_file",
                CommandKind.Script => "command_file",
                _ => "syntax_error",
            };
            _cmdFace[name] = face;
        }
        return face;
    }

    PathKind PathKindOf(string value)
    {
        if (!_paths.TryGetValue(value, out var kind))
        {
            kind = _shell.ResolvePath(value);
            _paths[value] = kind;
        }
        return kind;
    }

    static bool IsUrl(string v) =>
        v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// For history suggestions (complete_auto_history_validate): false if the line has a command
    /// name that doesn't exist (with <paramref name="commands"/>) or a path argument that doesn't
    /// exist (with <paramref name="paths"/>): the words Highlight marks syntax_error and
    /// filename_warning, plus words that were files or directories in a directory the entry ran in
    /// (<paramref name="ranIn"/>). Expressions, variables and wildcards are not checked.
    /// </summary>
    public bool IsValid(string text, bool commands, bool paths, IReadOnlyList<string>? ranIn = null)
    {
        Token[] tokens;
        try { tokens = Parse(text).Tokens; } catch { return true; }
        Token? prev = null;
        foreach (var t in tokens)
        {
            bool functionName = prev != null && prev.Kind is TokenKind.Function or TokenKind.Filter or TokenKind.Workflow;
            prev = t;
            if (functionName || t.Kind is not (TokenKind.Generic or TokenKind.Identifier or TokenKind.StringLiteral)) continue;
            var value = t is StringToken st ? st.Value : t.Text;
            if ((t.TokenFlags & TokenFlags.CommandName) != 0)
            {
                // quoted names are not marked by Highlight either
                if (commands && t.Kind != TokenKind.StringLiteral && CommandFace(value) == "syntax_error") return false;
                continue;
            }
            // plain words like "dev" are Identifier tokens
            if (!paths || (t.TokenFlags & (TokenFlags.Keyword | TokenFlags.TypeName | TokenFlags.AttributeName | TokenFlags.MemberName)) != 0) continue;
            if (value.Length == 0 || IsUrl(value)) continue;
            if (PathKindOf(value) != PathKind.None) continue;
            if (IsMissingPath(value) || WasPathIn(value, ranIn)) return false;
        }
        return true;
    }

    // Whether a word was a file or directory in one of the (last 4) directories a history entry ran
    // in. Then it was a path even if it doesn't look like one ("cd dev" run in ~), whatever the command.
    bool WasPathIn(string value, IReadOnlyList<string>? dirs)
    {
        if (dirs == null) return false;
        for (int i = dirs.Count - 1; i >= Math.Max(0, dirs.Count - 4); i--)
        {
            var key = (dirs[i], value);
            if (!_originPaths.TryGetValue(key, out var was))
                _originPaths[key] = was = FileSystemPaths.Resolve(value, dirs[i]) != PathKind.None;
            if (was) return true;
        }
        return false;
    }

    public CommandKind ResolveCommandKind(string name) => _shell.ResolveCommand(name);

    static bool LooksLikePath(string v) =>
        v.IndexOfAny(['\\', '/']) >= 0 || v.StartsWith('.') || v.StartsWith('~') || (v.Length >= 2 && v[1] == ':' && char.IsLetter(v[0]));

    // A path-like argument that does not exist. Plain relative words like origin/main only count
    // when their first segment exists (so branch names and the like are not flagged).
    bool IsMissingPath(string v)
    {
        if (v.Length < 2 || v == ".." || v.IndexOfAny(['*', '?', '[', '$']) >= 0 || !LooksLikePath(v)) return false;
        bool anchored = v.StartsWith('.') || v.StartsWith('~') || v.StartsWith('\\') || v.StartsWith('/') || (v[1] == ':' && char.IsLetter(v[0]));
        if (anchored) return true;
        int sep = v.IndexOfAny(['\\', '/']);
        var first = v.Substring(0, sep);
        if (first.Length == 0) return false;
        if (!_paths.TryGetValue(first, out var k)) { k = _shell.ResolvePath(first); _paths[first] = k; }
        return k == PathKind.Directory || k == PathKind.Link;
    }

    void ApplyFilename(string value, string[] faces, int s, int e, bool quoted)
    {
        if (!Options.GetBool("highlight_filename") || value.Length == 0) return;
        string face;
        if (IsUrl(value))
            face = "filename_url";
        else
        {
            face = PathKindOf(value) switch
            {
                PathKind.Directory => "filename_directory",
                PathKind.Link => "filename_link",
                PathKind.Executable => "filename_executable",
                PathKind.File => "filename_other",
                // a path-like argument that does not exist (wildcards are patterns, not paths)
                _ => IsMissingPath(value) ? "filename_warning" : "",
            };
        }
        if (face.Length == 0) return;
        // inside quotes only the content is marked; the quotes keep their face
        if (quoted) Fill(faces, s + 1, e - 1, face);
        else Fill(faces, s, e, face);
    }
}
