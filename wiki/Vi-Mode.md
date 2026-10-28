Turn it on with `bleopt default_keymap=vi` (or `ble-import keymap.vi`). The line starts in
insert mode; `ESC` goes to normal mode.

## Modes

- **Normal**, **insert**, **replace** (`R`), one command from insert (`C-o`)
- **Visual**: characterwise `v`, linewise `V`, blockwise `C-v`
- **Select** (`gh`, `gH`, or Shift+arrows): typing replaces the selection; `C-g` switches between
  select and visual

The line under the command line shows the mode (`-- INSERT --`, `-- VISUAL LINE --`, `~` in
normal mode). Hide it with `bleopt keymap_vi_mode_show=`. The cursor shape changes per mode; see
`keymap_vi_*_cursor` in [Options](Options).

## Commands

| | |
|---|---|
| Motions | `h l w b e W B E ge 0 ^ $ f F t T ; , % \| G gg j k + -` |
| Operators | `d c y < > g~ gu gU` with motions, text objects and counts |
| Text objects | `iw aw iW aW i" a" i' a' i( a( i{ a{ i[ a[ i< a<` |
| Editing | `x X r R ~ J s S C D Y p P o O i a I A`, `C-a` / `C-x` (increment / decrement number) |
| Undo | `u`, `C-r`, `.` repeats the last change, including what was typed in insert mode |
| Registers | `"a`-`"z`, `"A` appends, `"0`-`"9`, `"+` system clipboard, `"_` discards |
| Macros | `qa` ... `q` records, `@a` plays, `@@` repeats, `@:` repeats the last `:` command |
| Marks | `ma`, `'a`, `` `a ``, `''` |
| Search | `/` `?` `n` `N` search the history |
| Help | `K` or `f1` for the command under the cursor |

In visual block mode, `I`, `A` and `c` repeat what you type on every line of the block.

## Ex commands

`:s/pat/rep/g` with ranges `%`, `N,M`, `'<,'>`, `'a`; `:d`, `:y`, `:j`, `:>`, `:<`, `:normal`,
`:set`, `:undo`. `:w` runs the line. `:q` exits PowerShell, as in ble.sh.

`C-c` in normal mode does not discard the line; it shows a hint about `:q`. To clear the line use
`S` or `cc`.

## Copying to the system clipboard

Use the `"+` register: `"+yy`, `"+p`. A common shortcut is visual-mode `Y`:

```powershell
ble-bind -m vi_xmap -s 'Y' '" + y'
```
