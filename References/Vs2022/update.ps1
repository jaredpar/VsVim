$src = "C:\Program Files\Microsoft Visual Studio\2022\Public Preview"
$all = @(
  "Common7\IDE\Microsoft.VisualStudio.Platform.WindowManagement.dll",
  "Common7\IDE\Microsoft.VisualStudio.Shell.ViewManager.dll",
  "Common7\IDE\PrivateAssemblies\Microsoft.VisualStudio.Diagnostics.Assert.dll",
  "Common7\IDE\CommonExtensions\Microsoft\Editor\Microsoft.VisualStudio.Platform.VSEditor.dll",
  "Common7\IDE\CommonExtensions\Microsoft\Editor\Microsoft.VisualStudio.Text.Internal.dll"
)

foreach ($item in $all) {
  Copy-Item (Join-Path $src $item)
}