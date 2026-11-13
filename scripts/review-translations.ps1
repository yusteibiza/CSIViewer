param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
$overrides=@{
 en=@{
  'Audio del sistema'='System audio';'Portapapeles de texto'='Text clipboard';'Portapapeles'='Clipboard';
  'Audio y portapapeles'='Audio and clipboard';
  'Escuchar el audio del sistema (sin micrófono)'='Listen to system audio (no microphone)';
  'Compartir el portapapeles de texto en ambos sentidos'='Share text clipboard in both directions';
  'ID —'='ID —';'Configuración'='Settings';'Idioma'='Language';'Apariencia'='Appearance';'Color de énfasis'='Accent color';
  'Control remoto'='Remote control';'Transferencia de archivos'='File transfer';'Subir'='Up';'Unidades'='Drives';'Actualizar'='Refresh';
  'Reanudar'='Resume';'Reanudar interrumpidas'='Resume interrupted transfers';'Finalizar'='End';'Finalizar sesión'='End session';
  'Enviar →'='Send →';'← Recibir'='← Receive';'Carpeta'='Folder';'Archivo'='File';'Dispositivos'='Devices';
  'Activado'='On';'Desactivado'='Off';'Guardar y reconectar'='Save and reconnect';'Guardar configuración'='Save settings';
  '1. Abre la carpeta de origen en un panel y la carpeta de destino en el otro. Usa Unidades, Subir, Ir, Actualizar o escribe una ruta y pulsa Entrar. El doble clic solo abre carpetas: nunca ejecuta archivos.'='1. Open the source folder in one pane and the destination folder in the other. Use Drives, Up, Go and Refresh, or enter a path and press Enter. Double-click opens folders only; it never runs files.';
  'Abre origen y destino con Unidades, Subir, Ir o la ruta editable. Doble clic abre carpetas, nunca ejecuta archivos. Selecciona varios con Ctrl/Mayús. «Enviar →» copia del panel local al remoto y «← Recibir» del remoto al local. El destino es la carpeta abierta, no necesariamente Descargas; la vista de unidades no es un destino.'='Open the source and destination using Drives, Up, Go or the path field. Double-click opens folders, never files. Use Ctrl/Shift to select multiple items. Send → copies from the local pane to the remote pane; ← Receive copies in the opposite direction. The destination is the open folder, not necessarily Downloads. The drives view is not a destination.'
 }
 fr=@{
  'Audio del sistema'='Son du système';'Portapapeles de texto'='Presse-papiers texte';'Portapapeles'='Presse-papiers';
  'Audio y portapapeles'='Audio et presse-papiers';
  'Escuchar el audio del sistema (sin micrófono)'='Écouter le son du système (sans microphone)';
  'Compartir el portapapeles de texto en ambos sentidos'='Partager le presse-papiers texte dans les deux sens';
  'Probar'='Tester';'Actualizar estado'="Actualiser l’état";'Calidad de imagen'="Qualité d’image";
  'Captura moderna'='Capture moderne';'Modo desatendido'='Mode sans surveillance';
  'Preguntar siempre'='Toujours demander';'Rechazar automáticamente'='Refuser automatiquement';
  'ID —'='ID —';'Configuración'='Paramètres';'Idioma'='Langue';'Apariencia'='Apparence';'Color de énfasis'="Couleur d’accentuation";
  'Control remoto'='Contrôle à distance';'Transferencia de archivos'='Transfert de fichiers';'Subir'='Dossier parent';'Unidades'='Lecteurs';'Actualizar'='Actualiser';
  'Reanudar'='Reprendre';'Reanudar interrumpidas'='Reprendre les transferts interrompus';'Finalizar'='Terminer';'Finalizar sesión'='Terminer la session';
  'Enviar →'='Envoyer →';'← Recibir'='← Recevoir';'Carpeta'='Dossier';'Archivo'='Fichier';'Dispositivos'='Appareils';
  'Activado'='Activé';'Desactivado'='Désactivé';'Guardar y reconectar'='Enregistrer et reconnecter';'Guardar configuración'='Enregistrer les paramètres';
  '1. Abre la carpeta de origen en un panel y la carpeta de destino en el otro. Usa Unidades, Subir, Ir, Actualizar o escribe una ruta y pulsa Entrar. El doble clic solo abre carpetas: nunca ejecuta archivos.'="1. Ouvrez le dossier source dans un volet et le dossier de destination dans l’autre. Utilisez Lecteurs, Dossier parent, Aller et Actualiser, ou saisissez un chemin puis appuyez sur Entrée. Le double-clic ouvre uniquement les dossiers ; il n’exécute jamais de fichiers.";
  'Abre origen y destino con Unidades, Subir, Ir o la ruta editable. Doble clic abre carpetas, nunca ejecuta archivos. Selecciona varios con Ctrl/Mayús. «Enviar →» copia del panel local al remoto y «← Recibir» del remoto al local. El destino es la carpeta abierta, no necesariamente Descargas; la vista de unidades no es un destino.'="Ouvrez les dossiers source et de destination avec Lecteurs, Dossier parent, Aller ou le champ du chemin. Le double-clic ouvre les dossiers, jamais les fichiers. Utilisez Ctrl/Maj pour sélectionner plusieurs éléments. Envoyer → copie du volet local vers le volet distant ; ← Recevoir fait l’inverse. La destination est le dossier ouvert, pas forcément Téléchargements. La vue des lecteurs n’est pas une destination."
 }
 ca=@{
  'Audio'='Àudio';'Audio del sistema'='Àudio del sistema';'Portapapeles de texto'='Porta-retalls de text';'Portapapeles'='Porta-retalls';
  'Audio y portapapeles'='Àudio i porta-retalls';
  'Escuchar el audio del sistema (sin micrófono)'="Escoltar l’àudio del sistema (sense micròfon)";
  'Compartir el portapapeles de texto en ambos sentidos'='Compartir el porta-retalls de text en tots dos sentits';
  'ID —'='ID —';'Configuración'='Configuració';'Idioma'='Idioma';'Apariencia'='Aparença';'Color de énfasis'="Color d’èmfasi";
  'Control remoto'='Control remot';'Transferencia de archivos'='Transferència de fitxers';'Subir'='Carpeta superior';'Unidades'='Unitats';'Actualizar'='Actualitza';
  'Reanudar'='Reprèn';'Reanudar interrumpidas'='Reprèn les transferències interrompudes';'Finalizar'='Finalitza';'Finalizar sesión'='Finalitza la sessió';
  'Enviar →'='Envia →';'← Recibir'='← Rep';'Carpeta'='Carpeta';'Archivo'='Fitxer';'Dispositivos'='Dispositius';
  'Activado'='Activat';'Desactivado'='Desactivat';'Guardar y reconectar'='Desa i torna a connectar';'Guardar configuración'='Desa la configuració';
  '1. Abre la carpeta de origen en un panel y la carpeta de destino en el otro. Usa Unidades, Subir, Ir, Actualizar o escribe una ruta y pulsa Entrar. El doble clic solo abre carpetas: nunca ejecuta archivos.'="1. Obre la carpeta d’origen en un plafó i la de destinació a l’altre. Fes servir Unitats, Carpeta superior, Ves-hi i Actualitza, o escriu un camí i prem Retorn. El doble clic només obre carpetes; no executa mai fitxers.";
  'Abre origen y destino con Unidades, Subir, Ir o la ruta editable. Doble clic abre carpetas, nunca ejecuta archivos. Selecciona varios con Ctrl/Mayús. «Enviar →» copia del panel local al remoto y «← Recibir» del remoto al local. El destino es la carpeta abierta, no necesariamente Descargas; la vista de unidades no es un destino.'="Obre les carpetes d’origen i de destinació amb Unitats, Carpeta superior, Ves-hi o el camp del camí. El doble clic obre carpetes, mai fitxers. Fes servir Ctrl/Maj per seleccionar-ne diversos. Envia → copia del plafó local al remot; ← Rep ho fa en sentit contrari. La destinació és la carpeta oberta, no necessàriament Baixades. La vista d’unitats no és una destinació."
 }
}
$options=[Text.Json.JsonSerializerOptions]::new();$options.WriteIndented=$true;$options.Encoder=[Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
foreach($language in @('en','fr','ca')){
 $path=Join-Path $Root "src/CSIViewer.Core/Resources/$language.json"
 $document=[Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($path))
 $values=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
 foreach($property in $document.RootElement.EnumerateObject()){$values[$property.Name]=$property.Value.GetString()}
 $document.Dispose()
 foreach($key in $overrides[$language].Keys){$values[$key]=$overrides[$language][$key]}
 foreach($key in @($values.Keys)){
   $leading=[regex]::Match($key,'^\s*').Value;$trailing=[regex]::Match($key,'\s*$').Value
   $values[$key]=$leading+$values[$key].Trim()+$trailing
 }
 [IO.File]::WriteAllText($path,[Text.Json.JsonSerializer]::Serialize($values,$options))
}
