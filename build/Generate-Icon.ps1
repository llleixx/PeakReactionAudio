param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(20,30,43))
$white = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(241,245,249)),9
$orange = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,174,66)),9
$family = New-Object System.Drawing.FontFamily 'Segoe UI'
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(241,245,249))
function Draw-CenteredLabel([string]$Text, [float]$Size, [float]$CenterY) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $matrix = New-Object System.Drawing.Drawing2D.Matrix
    try {
        $path.AddString($Text,$family,[int][System.Drawing.FontStyle]::Bold,$Size,
            [System.Drawing.PointF]::Empty,[System.Drawing.StringFormat]::GenericTypographic)
        $bounds = $path.GetBounds()
        $matrix.Translate((128 - $bounds.X - $bounds.Width / 2),($CenterY - $bounds.Y - $bounds.Height / 2))
        $path.Transform($matrix)
        $graphics.FillPath($brush,$path)
    }
    finally { $matrix.Dispose(); $path.Dispose() }
}
try {
    $graphics.TranslateTransform(7,-5)
    $graphics.DrawLines($white, [System.Drawing.Point[]]@((New-Object System.Drawing.Point 33,132),(New-Object System.Drawing.Point 94,46),(New-Object System.Drawing.Point 151,132)))
    $graphics.DrawLine($white,94,46,112,104)
    $graphics.DrawArc($orange,132,61,45,65,-65,130)
    $graphics.DrawArc($orange,139,42,70,103,-65,130)
    $graphics.ResetTransform()
    Draw-CenteredLabel 'PEAK' 36 177
    Draw-CenteredLabel 'REACTION AUDIO' 18 216
    $bitmap.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $brush.Dispose(); $family.Dispose(); $orange.Dispose(); $white.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
