# Генерирует уникальные иконки ленты SmartNavisTools (16x16 и 32x32).
Add-Type -AssemblyName System.Drawing

function New-GradientBackground {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Size
    )

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 0, 94, 150),
        [System.Drawing.Color]::FromArgb(255, 13, 169, 202),
        45.0)
    $Graphics.FillRectangle($brush, $rect)
    $brush.Dispose()
}

function Save-ToolIcon {
    param(
        [string]$Path,
        [int]$Size,
        [scriptblock]$Draw
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    New-GradientBackground -Graphics $graphics -Size $Size
    & $Draw $graphics $Size
    $iconHandle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $icon.Save($stream)
    $stream.Close()
    $graphics.Dispose()
    $bitmap.Dispose()
    $icon.Dispose()
}

function Draw-ClashStatusIcon {
    param([System.Drawing.Graphics]$G, [int]$Size)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(2, [int]($Size * 0.12)))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cx = $Size * 0.42
    $cy = $Size * 0.40
    $r = $Size * 0.28
    $G.DrawArc($pen, $cx - $r, $cy - $r, $r * 2, $r * 2, 300, 240)
    $G.DrawLine($pen, ($cx + $r * 0.55), ($cy - $r * 0.75), ($cx + $r * 0.95), ($cy - $r * 1.05))
    $G.DrawLine($pen, ($cx + $r * 0.55), ($cy - $r * 0.75), ($cx + $r * 0.35), ($cy - $r * 1.05))

    $checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 120, 230, 180), [Math]::Max(2, [int]($Size * 0.11)))
    $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $G.DrawLine($checkPen, ($Size * 0.22), ($Size * 0.58), ($Size * 0.38), ($Size * 0.74))
    $G.DrawLine($checkPen, ($Size * 0.38), ($Size * 0.74), ($Size * 0.72), ($Size * 0.30))
    $pen.Dispose()
    $checkPen.Dispose()
}

function Draw-ClashTestsIcon {
    param([System.Drawing.Graphics]$G, [int]$Size)

    $white = [System.Drawing.Brushes]::White
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 180, 235, 255))
    $box = [Math]::Max(2, [int]($Size * 0.14))
    $left = [int]($Size * 0.18)
    $lineLeft = [int]($Size * 0.40)
    $lineW = [int]($Size * 0.42)
    $lineH = [Math]::Max(2, [int]($Size * 0.08))
    $y1 = [int]($Size * 0.24)
    $y2 = [int]($Size * 0.46)
    $y3 = [int]($Size * 0.68)

    $G.FillRectangle($white, $left, $y1, $box, $box)
    $G.FillRectangle($accent, $left, $y2, $box, $box)
    $G.FillRectangle($white, $left, $y3, $box, $box)
    $G.FillRectangle($white, $lineLeft, ($y1 + $box / 3), $lineW, $lineH)
    $G.FillRectangle($white, $lineLeft, ($y2 + $box / 3), ($lineW * 0.75), $lineH)
    $G.FillRectangle($white, $lineLeft, ($y3 + $box / 3), ($lineW * 0.55), $lineH)
    $accent.Dispose()
}

function Draw-SearchSetsProIcon {
    param([System.Drawing.Graphics]$G, [int]$Size)

    $white = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(2, [int]($Size * 0.10)))
    $white.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cx = $Size * 0.40
    $cy = $Size * 0.40
    $r = $Size * 0.22
    $G.DrawEllipse($white, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
    $G.DrawLine($white, ($cx + $r * 0.65), ($cy + $r * 0.65), ($Size * 0.78), ($Size * 0.78))

    $dot = [Math]::Max(2, [int]($Size * 0.07))
    $gridBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))
    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 3; $col++) {
            $x = [int]($Size * 0.16 + $col * $Size * 0.11)
            $y = [int]($Size * 0.16 + $row * $Size * 0.11)
            $G.FillRectangle($gridBrush, $x, $y, $dot, $dot)
        }
    }

    $white.Dispose()
    $gridBrush.Dispose()
}

function Draw-ClashDashboardIcon {
    param([System.Drawing.Graphics]$G, [int]$Size)

    $white = [System.Drawing.Brushes]::White
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 180, 235, 255))
    $barW = [Math]::Max(2, [int]($Size * 0.14))
    $G.FillRectangle($white, [int]($Size * 0.18), [int]($Size * 0.42), $barW, [int]($Size * 0.36))
    $G.FillRectangle($accent, [int]($Size * 0.40), [int]($Size * 0.28), $barW, [int]($Size * 0.50))
    $G.FillRectangle($white, [int]($Size * 0.62), [int]($Size * 0.15), $barW, [int]($Size * 0.63))
    $G.DrawLine([System.Drawing.Pens]::White, [int]($Size * 0.14), [int]($Size * 0.82), [int]($Size * 0.82), [int]($Size * 0.82))
    $accent.Dispose()
}

$iconsRoot = Join-Path $PSScriptRoot "..\Images"
$iconsRoot = [System.IO.Path]::GetFullPath($iconsRoot)
if (-not (Test-Path $iconsRoot)) {
    New-Item -ItemType Directory -Path $iconsRoot -Force | Out-Null
}

$tools = @(
    @{ Name = "ClashStatus"; Draw = ${function:Draw-ClashStatusIcon} },
    @{ Name = "ClashTests"; Draw = ${function:Draw-ClashTestsIcon} },
    @{ Name = "SearchSetsPro"; Draw = ${function:Draw-SearchSetsProIcon} },
    @{ Name = "ClashDashboard"; Draw = ${function:Draw-ClashDashboardIcon} }
)

foreach ($tool in $tools) {
    $smallPath = Join-Path $iconsRoot ($tool.Name + "Icon_Small.ico")
    $largePath = Join-Path $iconsRoot ($tool.Name + "Icon_Large.ico")
    Save-ToolIcon -Path $smallPath -Size 16 -Draw $tool.Draw
    Save-ToolIcon -Path $largePath -Size 32 -Draw $tool.Draw
    Write-Host "Created $($tool.Name) icons"
}

# Общая иконка пакета — дашборд как основной символ набора.
Copy-Item (Join-Path $iconsRoot "ClashDashboardIcon_Small.ico") (Join-Path $iconsRoot "SmartNavisToolsIcon_Small.ico") -Force
Copy-Item (Join-Path $iconsRoot "ClashDashboardIcon_Large.ico") (Join-Path $iconsRoot "SmartNavisToolsIcon_Large.ico") -Force
Write-Host "Done. Icons folder: $iconsRoot"
