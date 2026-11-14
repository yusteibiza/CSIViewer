param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$sources=@(Get-Content (Join-Path $Root 'artifacts/diagnostics/localization-sources.json') -Raw -Encoding utf8|ConvertFrom-Json)
$destination=Join-Path $Root 'src/CSIViewer.Core/Resources'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
$options=[Text.Json.JsonSerializerOptions]::new();$options.WriteIndented=$true;$options.Encoder=[Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$spanish=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal);foreach($s in $sources){$spanish[$s]=$s}
[IO.File]::WriteAllText((Join-Path $destination 'es.json'),[Text.Json.JsonSerializer]::Serialize($spanish,$options))
foreach($language in @('en','fr','ca')){
 $path=Join-Path $destination ($language+'.json')
 $translated=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
 if(Test-Path $path){
   $document=[Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($path))
   foreach($property in $document.RootElement.EnumerateObject()){$translated[$property.Name]=$property.Value.GetString()}
   $document.Dispose()
 }
 $pending=@($sources|Where-Object {-not $translated.ContainsKey($_)})
 $batches=[Collections.Generic.List[object]]::new();$current=[Collections.Generic.List[string]]::new();$bytes=0
 foreach($s in $pending){$length=[Text.Encoding]::UTF8.GetByteCount($s)+20;if($bytes+$length -gt 2500 -and $current.Count){$batches.Add($current.ToArray());$current.Clear();$bytes=0};$current.Add($s);$bytes+=$length}
 if($current.Count){$batches.Add($current.ToArray())}
 foreach($batch in $batches){
   $protected=[Collections.Generic.List[string]]::new()
   $parts=[Collections.Generic.List[string]]::new()
   for($i=0;$i-lt $batch.Count;$i++){
     $safe=[regex]::Replace($batch[$i], '\{\d+[^}]*\}|https?://[^\s)»]+|CSIViewerServidor(?:\.exe)?|CSIViewer\.Host(?:\.Agent)?(?:\.exe)?|CSIViewer(?: Host| Servidor)?(?:\.exe)?|--[a-z-]+(?:=[a-z0-9_.:/\\-]+)?|/api/\w+|\bDEV-\d+\b', {
       param($m)
       $index=$protected.Count;$protected.Add($m.Value);return "ZXKEEP$($index.ToString('D4'))ZX"
     })
     $parts.Add("[P$($i.ToString('D4'))]`n$safe")
   }
   # Only generic, source-controlled UI strings are sent. Runtime settings,
   # passwords, logs, databases and user text are never read by this generator.
   $query=[Uri]::EscapeDataString(($parts -join "`n"))
   $url="https://translate.googleapis.com/translate_a/single?client=gtx&sl=es&tl=$language&dt=t&q=$query"
   $response=$null
   for($attempt=0;$attempt-lt 4;$attempt++){
     try{$response=Invoke-RestMethod $url -TimeoutSec 45;break}catch{if($attempt-eq 3){throw};Start-Sleep -Seconds ([math]::Pow(2,$attempt))}
   }
   $text=($response[0]|ForEach-Object {$_[0]}) -join ''
   $matches=[regex]::Matches($text,'\[\s*P\s*(\d{4})\s*\]\s*([\s\S]*?)(?=\[\s*P\s*\d{4}\s*\]|$)')
   if($matches.Count-ne $batch.Count){throw "Translation batch markers changed ($language): expected $($batch.Count), found $($matches.Count)"}
   foreach($match in $matches){
     $index=[int]$match.Groups[1].Value
     $value=$match.Groups[2].Value.Trim()
     for($p=0;$p-lt $protected.Count;$p++){
       $value=[regex]::Replace($value,"ZX\s*KEEP\s*$($p.ToString('D4'))\s*ZX",[Text.RegularExpressions.MatchEvaluator]{param($m)$protected[$p]},[Text.RegularExpressions.RegexOptions]::IgnoreCase)
     }
     if($value-match 'ZX\s*KEEP'){throw "Unrestored placeholder in $language"}
     $original=$batch[$index]
     $expected=@([regex]::Matches($original,'\{\d+[^}]*\}')|ForEach-Object {$_.Value}|Sort-Object)
     $actual=@([regex]::Matches($value,'\{\d+[^}]*\}')|ForEach-Object {$_.Value}|Sort-Object)
     if(($expected-join '|')-ne ($actual-join '|')){throw "Format placeholders changed in $language"}
     $translated[$original]=$value
   }
   $ordered=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal);foreach($source in $sources){if($translated.ContainsKey($source)){$ordered[$source]=$translated[$source]}}
   [IO.File]::WriteAllText($path,[Text.Json.JsonSerializer]::Serialize($ordered,$options))
 }
  # Also prune retired UI keys when no translation batches were needed.
  $ordered=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
  foreach($source in $sources){if($translated.ContainsKey($source)){$ordered[$source]=$translated[$source]}}
  [IO.File]::WriteAllText($path,[Text.Json.JsonSerializer]::Serialize($ordered,$options))
  Write-Output "$language resources=$($ordered.Count)"
}
