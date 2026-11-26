<#
.SYNOPSIS
    Renders the Nexer logo as a Turbo Vision-style scene using 24-bit ANSI colour
    and standard box drawing characters, in the classic Borland palette.
.PARAMETER OutFile
    Optional path to also write the raw ANSI output (e.g. nexer-logo.ans) so it can be
    displayed later with `cat` / `Get-Content -Raw`.
.PARAMETER LogoOnly
    Emit only the letterforms on a blue background, without the menu bar, window and status line.
#>
[CmdletBinding()]
param(
    [string] $OutFile,
    [switch] $LogoOnly
)

# Classic Borland/CGA palette, expressed as 24-bit RGB.
$Blue     = 0x00, 0x00, 0xAA
$Gray     = 0xAA, 0xAA, 0xAA
$Black    = 0x00, 0x00, 0x00
$DarkGray = 0x55, 0x55, 0x55
$White    = 0xFF, 0xFF, 0xFF
$Yellow   = 0xFF, 0xFF, 0x55
$Cyan     = 0x55, 0xFF, 0xFF
$Red      = 0xAA, 0x00, 0x00
$Green    = 0x55, 0xFF, 0x55

$E = [char]27
function Fg($c) { "$E[38;2;$($c[0]);$($c[1]);$($c[2])m" }
function Bg($c) { "$E[48;2;$($c[0]);$($c[1]);$($c[2])m" }
$Reset = "$E[0m"

# 5x5 pixel font; pixel edges are traced with double-line box drawing characters.
$font = @{
    'N' = 'X...X', 'XX..X', 'X.X.X', 'X..XX', 'X...X'
    'E' = 'XXXXX', 'X....', 'XXXX.', 'X....', 'XXXXX'
    'X' = 'X...X', '.X.X.', '..X..', '.X.X.', 'X...X'
    'R' = 'XXXXX', 'X...X', 'XXXXX', 'X..X.', 'X...X'
}

function Get-Letterforms([string] $word) {
    $bmp = foreach ($r in 0..4) { ($word.ToCharArray() | ForEach-Object { $font[[string]$_][$r] }) -join '.' }
    $h = $bmp.Count; $w = $bmp[0].Length
    $on = { param($x, $y) $x -ge 0 -and $y -ge 0 -and $x -lt $w -and $y -lt $h -and $bmp[$y][$x] -eq 'X' }
    # Key: edge present going up, down, left, right from a pixel corner.
    $glyph = @{
        '1100' = [char]0x2551; '0011' = [char]0x2550; '0101' = [char]0x2554; '0110' = [char]0x2557
        '1001' = [char]0x255A; '1010' = [char]0x255D; '1101' = [char]0x2560; '1110' = [char]0x2563
        '0111' = [char]0x2566; '1011' = [char]0x2569; '1111' = [char]0x256C; '0000' = ' '
    }
    foreach ($cy in 0..$h) {
        $sb = [Text.StringBuilder]::new()
        foreach ($cx in 0..$w) {
            $tl = & $on ($cx - 1) ($cy - 1); $tr = & $on $cx ($cy - 1)
            $bl = & $on ($cx - 1) $cy;       $br = & $on $cx $cy
            $key = '{0}{1}{2}{3}' -f [int]($tl -ne $tr), [int]($bl -ne $br), [int]($tl -ne $bl), [int]($tr -ne $br)
            [void]$sb.Append($glyph[$key])
            if ($cx -lt $w) { [void]$sb.Append($(if ($tr -ne $br) { [char]0x2550 } else { ' ' })) }
        }
        $sb.ToString()
    }
}

# Turbo Vision-style hotkey markup: text between ~tildes~ is drawn in red.
function Hot([string] $text, $fg, $bg) {
    (Bg $bg) + (Fg $fg) + ($text -replace '~([^~]+)~', "$(Fg $Red)`$1$(Fg $fg)")
}
function VisibleLength([string] $text) { ($text -replace '~', '').Length }

$letters = @(Get-Letterforms 'NEXER')
$width   = 80
$lines   = [Collections.Generic.List[string]]::new()

if ($LogoOnly) {
    foreach ($l in $letters) { $lines.Add((Bg $Blue) + (Fg $Yellow) + " $l " + $Reset) }
}
else {
    $shade   = [string][char]0x2591
    $desktop = { param($n) (Bg $Blue) + (Fg $Gray) + ($shade * $n) }
    $hz = [string][char]0x2550; $vt = [string][char]0x2551

    $menu = " ~$([char]0x2261)~  ~F~ile  ~E~dit  ~S~earch  ~V~iew  ~W~indow  ~H~elp"
    $lines.Add((Hot $menu $Black $Gray) + (' ' * ($width - (VisibleLength $menu))) + $Reset)

    $inner = $letters[0].Length + 6
    $left  = [int](($width - $inner - 4) / 2)
    $right = $width - $left - $inner - 4   # 2 frame columns + 2 shadow columns
    $frame = (Bg $Blue) + (Fg $White)
    $shadowCols = (Bg $Black) + (Fg $DarkGray) + '  '

    $center = { param($t, $n) $p = $n - $t.Length; (' ' * [math]::Floor($p / 2)) + $t + (' ' * [math]::Ceiling($p / 2)) }
    $row = { param($content) (& $desktop $left) + $frame + $vt + $content + $frame + $vt + $shadowCols + (& $desktop $right) + $Reset }

    $lines.Add((& $desktop $width) + $Reset)

    $title    = ' Nexer '
    $closeBtn = "[$(Fg $Green)$([char]0x25A0)$(Fg $White)]"
    $topFill  = $inner - 5 - $title.Length
    $lines.Add((& $desktop $left) + $frame + [char]0x2554 + $hz + $closeBtn + ($hz * [math]::Floor($topFill / 2)) +
               $title + ($hz * [math]::Ceiling($topFill / 2)) + $hz + [char]0x2557 + (& $desktop ($right + 2)) + $Reset)

    $lines.Add((& $row (' ' * $inner)))
    foreach ($l in $letters) { $lines.Add((& $row ((Fg $Yellow) + (& $center $l $inner)))) }
    $lines.Add((& $row (' ' * $inner)))
    $lines.Add((& $row ((Fg $Cyan) + (& $center 'A Turbo Vision-style text UI framework for .NET' $inner))))
    $lines.Add((& $row (' ' * $inner)))

    $lines.Add((& $desktop $left) + $frame + [char]0x255A + ($hz * $inner) + [char]0x255D + $shadowCols + (& $desktop $right) + $Reset)
    $lines.Add((& $desktop ($left + 2)) + (Bg $Black) + (' ' * ($inner + 2)) + (& $desktop $right) + $Reset)
    $lines.Add((& $desktop $width) + $Reset)

    $status = ' ~Alt-X~ Exit  ~F1~ Help  ~F10~ Menu'
    $lines.Add((Hot $status $Black $Gray) + (' ' * ($width - (VisibleLength $status))) + $Reset)
}

$output = $lines -join [Environment]::NewLine
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Write-Output $output

if ($OutFile) {
    [IO.File]::WriteAllText($OutFile, $output + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
