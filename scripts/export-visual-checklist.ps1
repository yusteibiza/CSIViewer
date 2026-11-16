$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$out=Join-Path $root 'artifacts/audit/manual-ui'
New-Item -ItemType Directory -Force $out|Out-Null
Add-Type -AssemblyName System.Xml.Linq
$events='^(Click|Loaded|Unloaded|Closing|Closed|Activated|SelectionChanged|TextChanged|Toggled|ValueChanged|SizeChanged|ActualThemeChanged|ItemClick|DoubleTapped|Tapped|RightTapped|Pointer\w+|KeyDown|KeyUp|GotFocus|LostFocus|Drag\w+|Drop|Navigation\w+|BackRequested|Opened|Opening|PasswordChanged|BeforeTextChanging|TextChanging)$'
$controls=@(Get-ChildItem (Join-Path $root 'src') -Filter *.xaml -Recurse|Where-Object{$_.FullName-notmatch '[\\/](bin|obj)[\\/]'}|ForEach-Object{
    $file=$_
    $xml=[Xml.Linq.XDocument]::Load($file.FullName,[Xml.Linq.LoadOptions]::SetLineInfo)
    foreach($element in $xml.Descendants()){
        $handlers=@($element.Attributes()|Where-Object{$_.Name.NamespaceName-eq '' -and $_.Name.LocalName-match $events}|ForEach-Object{"$($_.Name.LocalName)=$($_.Value)"})
        [pscustomobject]@{File=[IO.Path]::GetRelativePath($root,$file.FullName);Line=([Xml.IXmlLineInfo]$element).LineNumber;
            Control=$element.Name.LocalName;Name=$element.Attribute('{http://schemas.microsoft.com/winfx/2006/xaml}Name').Value;
            Label=($element.Attribute('Content').Value+$element.Attribute('Text').Value);Events=$handlers-join ';';
            Command=$element.Attribute('Command').Value;Light='PENDIENTE';Dark='PENDIENTE';System='PENDIENTE';
            KeyboardDPI='PENDIENTE';DoubleActionAndClose='PENDIENTE';Result='NO EJECUTADA';Notes=''}
    }
})
$controls|Export-Csv (Join-Path $out 'controls-and-events.csv') -NoTypeInformation -Encoding utf8
$dialogs=@(Get-ChildItem (Join-Path $root 'src') -Filter *.cs -Recurse|Where-Object{$_.FullName-notmatch '[\\/](bin|obj)[\\/]'}|ForEach-Object{
    $file=$_;$text=[IO.File]::ReadAllText($file.FullName)
    foreach($match in [regex]::Matches($text,'new\s+(ContentDialog|FolderPicker|FileOpenPicker|FileSavePicker|Flyout|MenuFlyout)\b')){
        [pscustomobject]@{File=[IO.Path]::GetRelativePath($root,$file.FullName);Line=1+([regex]::Matches($text.Substring(0,$match.Index),'\n')).Count;
            Kind=$match.Groups[1].Value;OpenCancelConfirmError='PENDIENTE';ThemeChange='PENDIENTE';CloseWhilePending='PENDIENTE';Result='NO EJECUTADA';Notes=''}
    }
})
$dialogs|Export-Csv (Join-Path $out 'dialogs-and-pickers.csv') -NoTypeInformation -Encoding utf8
Write-Host "$($controls.Count) elementos XAML, $($dialogs.Count) construcciones de diálogo/picker/flyout. Plantilla manual, no resultados ejecutados: $out"
