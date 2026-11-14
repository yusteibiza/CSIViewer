param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
$overrides=@{
 en=@{
  'Se borrará el historial de chat de {0}. ¿Continuar?'='The chat history for {0} will be deleted. Continue?';
  '¿Quieres eliminar «{0}» ({1}) de tus dispositivos guardados? También se borrará su historial de chat.'='Delete “{0}” ({1}) from your saved devices? Its chat history will also be deleted.';
  'Selecciona un dispositivo para consultar solo su conversación, incluso sin conexión. Borrar chat pide confirmación y elimina su historial local. Al eliminar un dispositivo también se borra su chat y desaparece del selector. Exportar chat guarda toda la conversación en un archivo de texto UTF-8. Los emojis están organizados por categorías. Para enviar mensajes necesitas una sesión con permiso Chat.'='Select a device to view only its conversation, even offline. Delete chat asks for confirmation and deletes its local history. Deleting a device also deletes its chat and removes it from the selector. Export chat saves the entire conversation as a UTF-8 text file. Emojis are organized by category. Sending messages requires a session with Chat permission.';
  'Captura moderna sin respuesta; recuperando la imagen con GDI.'='Modern capture is not responding; recovering the image with GDI.';
  'Activado (en ejecución)'='Enabled (running)';
  'Arranca con Windows mediante una tarea programada. Consultar el estado no requiere UAC; cambiar el inicio requiere administración.'='Starts with Windows through a scheduled task. Checking status does not require UAC; changing startup requires administrator rights.';
  'Falta el agente de arranque: {0}. Repara la instalación de CSIViewer Host.'='The startup agent is missing: {0}. Repair the CSIViewer Host installation.';
  'No existe el archivo o la carpeta.'='The file or folder does not exist.';
  'El agente debe estar instalado en Program Files.'='The agent must be installed in Program Files.';
  'La ruta contiene un enlace o una redirección.'='The path contains a link or redirection.';
  'El propietario no es SYSTEM, Administradores ni TrustedInstaller.'='The owner is not SYSTEM, Administrators or TrustedInstaller.';
  'Una cuenta no administrativa puede modificar la instalación.'='A non-administrator account can modify the installation.';
  'Windows no permite leer los permisos de esta ruta.'='Windows does not allow reading the permissions for this path.';
  'No se puede activar el inicio. Ruta: {0}. Motivo: {1}'='Startup cannot be enabled. Path: {0}. Reason: {1}';
  'No se pueden comprobar los permisos de {0}. No se ha modificado el inicio automático.'='The permissions for {0} could not be checked. Startup has not been changed.';
  'Valores por defecto'='Default values';'Restablecer'='Revert changes';
  'Iniciar servidor y Crear servicio utilizan la configuración guardada.'='Start server and Create service use the saved settings.';
  'Estado no accesible. Actualiza con permisos de administrador.'='Status unavailable. Refresh with administrator rights.';
  ' (último estado confirmado)'=' (last confirmed status)';
  'Para activar el inicio, instala CSIViewer Host para todos los usuarios en una carpeta protegida.'='To enable startup, install CSIViewer Host for all users in a protected folder.';
  'Permiso de administrador cancelado; no se ha cambiado nada.'='Administrator approval cancelled; nothing has changed.';
  'La operación no se ha confirmado. Código {0}. No se mostrará como activada sin verificar la tarea.'='The operation could not be confirmed. Code {0}. Startup will not be shown as enabled until the task is verified.';
  'Acceso al inicio de Windows'='Windows sign-in access';
  'Requiere administración y un ID de equipo distinto. HTTP remoto solo con autorización explícita.'='Requires administrator rights and a separate machine ID. Remote HTTP requires explicit approval.';
  'Instala CSIViewer Host para todos los usuarios en la ruta predeterminada para configurar esta función.'='Install CSIViewer Host for all users in the default location to configure this feature.';
  'Conectado sin control de ratón y teclado'='Connected without mouse and keyboard control';
  'Otra sesión tiene el control de este equipo. Finaliza esa sesión y vuelve a conectar solicitando el permiso de control.'='Another session controls this computer. End that session and reconnect requesting control permission.';
  'El equipo remoto no ha concedido el permiso de control. Finaliza la sesión y vuelve a solicitar «Controlar ratón y teclado».'='The remote computer has not granted control permission. End the session and request “Control mouse and keyboard” again.';
  'Vista de {0} (sin control)'='Viewing {0} (no control)';
  'Sesión con {0}'='Session with {0}';
  'Equipos vinculados'='Linked computers';
  'En la ficha puedes añadir empresa, correo, teléfono y notas. Los favoritos aparecen primero. Abre Dispositivos vinculados para buscar y seleccionar varios equipos, o desvincularlos. Cancelar descarta los cambios; Guardar conserva los datos y los vínculos.'='The contact details can include company, email, phone and notes. Favorites appear first. Open Linked devices to search for and select multiple computers, or unlink them. Cancel discards changes; Save keeps the details and links.';
  'Empresa'='Company';'Correo electrónico'='Email';'Teléfono'='Phone';'Favorito'='Favorite';
  'Marcar como favorito'='Mark as favorite';'Dispositivo no guardado ({0})'='Unsaved device ({0})';
  'Seleccionar dispositivos…'='Select devices…';'1 dispositivo seleccionado'='1 device selected';
  '{0} dispositivos seleccionados'='{0} devices selected';'Buscar por nombre o ID…'='Search by name or ID…';
  'Puedes seleccionar varios equipos.'='You can select multiple computers.';'Desvincular todos'='Unlink all';
  'Completa la ficha y vincula los equipos del contacto.'="Complete the details and link the contact’s computers.";
  'Introduce un nombre para el contacto.'='Enter a name for the contact.';'Revisa el correo electrónico.'='Check the email address.';
  'Conexión en curso'='Connection in progress';'Listo para conectar'='Ready to connect';
  'Introduce el ID, revisa los permisos y pulsa Conectar.'='Enter the ID, review the permissions and click Connect.';
  'Esperando aceptación del equipo remoto…'='Waiting for the remote computer to accept…';
  'Conectando con el equipo remoto…'='Connecting to the remote computer…';
  'El equipo remoto está validando la conexión y los permisos solicitados. Espera a que la autorice.'='The remote computer is checking the connection and requested permissions. Please wait for authorization.';
  'Enviando la solicitud y estableciendo una conexión segura. Puede tardar unos instantes.'='Sending the request and establishing a secure connection. This may take a few moments.';
  'El chat del host es temporal y solo está disponible durante una sesión con permiso Chat. No tiene selector de dispositivo ni opciones de exportación o borrado manual. Cuando el host o el visor finaliza la sesión, los mensajes y el borrador se eliminan del host; también se vacían si se pierde la conexión. No se guarda el chat en disco. El visor conserva su historial por dispositivo. Puedes insertar emojis por categorías con el botón de la cara sonriente.'='Host chat is temporary and available only during a session with Chat permission. It has no device selector, export or manual delete options. When the host or viewer ends the session, messages and the draft are cleared from the host; they are also cleared if the connection is lost. Chat is not saved to disk. The viewer keeps its per-device history. Use the smiley button to insert emojis by category.';
  'Si el host está minimizado u oculto y la conexión requiere aprobación, su ventana se restaura para mostrar la solicitud de permisos. Debes aceptar o rechazar; mostrar la ventana no concede permisos automáticamente.'='If the host is minimized or hidden and a connection requires approval, its window is restored to show the permission request. You must accept or reject it; showing the window does not automatically grant permissions.';
  'Revisa los permisos'='Review permissions';
  'Selecciona los permisos, incluido audio o portapapeles si los necesitas, y pulsa Conectar.'='Select the permissions, including audio or clipboard if needed, then click Connect.';
  'Al elegir un dispositivo guardado se preparan el ID y la contraseña. Revisa los permisos antes de pulsar Conectar: audio y portapapeles son opcionales y deben solicitarse antes de iniciar la sesión.'='Selecting a saved device prefills its ID and password. Review the permissions before clicking Connect: audio and clipboard are optional and must be requested before starting the session.';
  'Dispositivo'='Device';'Borrar chat'='Delete chat';'Exportar chat'='Export chat';'Chat con {0}'='Chat with {0}';'Yo'='Me';
  'Más usados'='Frequently used';'Caras y emociones'='Faces and emotions';'Gestos y personas'='Gestures and people';
  'Animales y naturaleza'='Animals and nature';'Comida y bebida'='Food and drink';'Viajes y lugares'='Travel and places';
  'Actividades'='Activities';'Objetos y símbolos'='Objects and symbols';
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
  'Se borrará el historial de chat de {0}. ¿Continuar?'="L’historique du chat de {0} sera supprimé. Continuer ?";
  '¿Quieres eliminar «{0}» ({1}) de tus dispositivos guardados? También se borrará su historial de chat.'="Supprimer « {0} » ({1}) de vos appareils enregistrés ? Son historique de chat sera également supprimé.";
  'Selecciona un dispositivo para consultar solo su conversación, incluso sin conexión. Borrar chat pide confirmación y elimina su historial local. Al eliminar un dispositivo también se borra su chat y desaparece del selector. Exportar chat guarda toda la conversación en un archivo de texto UTF-8. Los emojis están organizados por categorías. Para enviar mensajes necesitas una sesión con permiso Chat.'="Sélectionnez un appareil pour consulter uniquement sa conversation, même hors ligne. Effacer le chat demande confirmation et supprime son historique local. Supprimer un appareil supprime aussi son chat et le retire du sélecteur. Exporter le chat enregistre toute la conversation dans un fichier texte UTF-8. Les émojis sont classés par catégorie. L’envoi de messages nécessite une session avec l’autorisation Chat.";
  'Captura moderna sin respuesta; recuperando la imagen con GDI.'="La capture moderne ne répond pas ; récupération de l’image avec GDI.";
  'Activado (en ejecución)'="Activé (en cours d’exécution)";
  'Arranca con Windows mediante una tarea programada. Consultar el estado no requiere UAC; cambiar el inicio requiere administración.'="Démarre avec Windows via une tâche planifiée. Consulter l’état ne nécessite pas l’UAC ; modifier le démarrage nécessite les droits administrateur.";
  'Falta el agente de arranque: {0}. Repara la instalación de CSIViewer Host.'="L’agent de démarrage est absent : {0}. Réparez l’installation de CSIViewer Host.";
  'No existe el archivo o la carpeta.'="Le fichier ou le dossier n’existe pas.";
  'El agente debe estar instalado en Program Files.'="L’agent doit être installé dans Program Files.";
  'La ruta contiene un enlace o una redirección.'='Le chemin contient un lien ou une redirection.';
  'El propietario no es SYSTEM, Administradores ni TrustedInstaller.'="Le propriétaire n’est ni SYSTEM, ni Administrateurs, ni TrustedInstaller.";
  'Una cuenta no administrativa puede modificar la instalación.'="Un compte non administrateur peut modifier l’installation.";
  'Windows no permite leer los permisos de esta ruta.'='Windows ne permet pas de lire les autorisations de ce chemin.';
  'No se puede activar el inicio. Ruta: {0}. Motivo: {1}'="Impossible d’activer le démarrage. Chemin : {0}. Motif : {1}";
  'No se pueden comprobar los permisos de {0}. No se ha modificado el inicio automático.'="Impossible de vérifier les autorisations de {0}. Le démarrage automatique n’a pas été modifié.";
  'Valores por defecto'='Valeurs par défaut';'Restablecer'='Annuler les modifications';
  'Iniciar servidor y Crear servicio utilizan la configuración guardada.'='Démarrer le serveur et Créer le service utilisent les paramètres enregistrés.';
  'Estado no accesible. Actualiza con permisos de administrador.'='État inaccessible. Actualisez avec les droits administrateur.';
  ' (último estado confirmado)'=' (dernier état confirmé)';
  'Para activar el inicio, instala CSIViewer Host para todos los usuarios en una carpeta protegida.'='Pour activer le démarrage, installez CSIViewer Host pour tous les utilisateurs dans un dossier protégé.';
  'Permiso de administrador cancelado; no se ha cambiado nada.'="Autorisation administrateur annulée ; aucune modification n’a été effectuée.";
  'La operación no se ha confirmado. Código {0}. No se mostrará como activada sin verificar la tarea.'="L’opération n’a pas été confirmée. Code {0}. Le démarrage ne sera pas indiqué comme activé sans vérification de la tâche.";
  'Acceso al inicio de Windows'='Accès à la connexion Windows';
  'Requiere administración y un ID de equipo distinto. HTTP remoto solo con autorización explícita.'='Nécessite les droits administrateur et un identifiant de machine distinct. HTTP distant uniquement avec autorisation explicite.';
  'Instala CSIViewer Host para todos los usuarios en la ruta predeterminada para configurar esta función.'='Installez CSIViewer Host pour tous les utilisateurs dans le dossier par défaut pour configurer cette fonction.';
  'Conectado sin control de ratón y teclado'='Connecté sans contrôle de la souris et du clavier';
  'Otra sesión tiene el control de este equipo. Finaliza esa sesión y vuelve a conectar solicitando el permiso de control.'='Une autre session contrôle cet ordinateur. Terminez cette session et reconnectez-vous en demandant le droit de contrôle.';
  'El equipo remoto no ha concedido el permiso de control. Finaliza la sesión y vuelve a solicitar «Controlar ratón y teclado».'="L’ordinateur distant n’a pas accordé le droit de contrôle. Terminez la session et redemandez « Contrôler la souris et le clavier ».";
  'Vista de {0} (sin control)'='Vue de {0} (sans contrôle)';
  'Sesión con {0}'='Session avec {0}';
  'Equipos vinculados'='Ordinateurs associés';
  'En la ficha puedes añadir empresa, correo, teléfono y notas. Los favoritos aparecen primero. Abre Dispositivos vinculados para buscar y seleccionar varios equipos, o desvincularlos. Cancelar descarta los cambios; Guardar conserva los datos y los vínculos.'="La fiche peut contenir une entreprise, une adresse e-mail, un téléphone et des notes. Les favoris apparaissent en premier. Ouvrez Appareils associés pour rechercher et sélectionner plusieurs ordinateurs ou les dissocier. Annuler abandonne les modifications ; Enregistrer conserve les données et les associations.";
  'Empresa'='Entreprise';'Correo electrónico'='Adresse e-mail';'Teléfono'='Téléphone';'Favorito'='Favori';
  'Marcar como favorito'='Marquer comme favori';'Dispositivo no guardado ({0})'='Appareil non enregistré ({0})';
  'Seleccionar dispositivos…'='Sélectionner des appareils…';'1 dispositivo seleccionado'='1 appareil sélectionné';
  '{0} dispositivos seleccionados'='{0} appareils sélectionnés';'Buscar por nombre o ID…'="Rechercher par nom ou ID…";
  'Puedes seleccionar varios equipos.'='Vous pouvez sélectionner plusieurs ordinateurs.';'Desvincular todos'='Tout dissocier';
  'Completa la ficha y vincula los equipos del contacto.'="Complétez la fiche et associez les ordinateurs du contact.";
  'Introduce un nombre para el contacto.'='Saisissez un nom pour le contact.';'Revisa el correo electrónico.'="Vérifiez l’adresse e-mail.";
  'Conexión en curso'='Connexion en cours';'Listo para conectar'='Prêt à se connecter';
  'Introduce el ID, revisa los permisos y pulsa Conectar.'="Saisissez l’ID, vérifiez les autorisations et cliquez sur Connecter.";
  'Esperando aceptación del equipo remoto…'="En attente de l’acceptation de l’ordinateur distant…";
  'Conectando con el equipo remoto…'="Connexion à l’ordinateur distant…";
  'El equipo remoto está validando la conexión y los permisos solicitados. Espera a que la autorice.'="L’ordinateur distant vérifie la connexion et les autorisations demandées. Veuillez attendre son autorisation.";
  'Enviando la solicitud y estableciendo una conexión segura. Puede tardar unos instantes.'="Envoi de la demande et établissement d’une connexion sécurisée. Cela peut prendre quelques instants.";
  'El chat del host es temporal y solo está disponible durante una sesión con permiso Chat. No tiene selector de dispositivo ni opciones de exportación o borrado manual. Cuando el host o el visor finaliza la sesión, los mensajes y el borrador se eliminan del host; también se vacían si se pierde la conexión. No se guarda el chat en disco. El visor conserva su historial por dispositivo. Puedes insertar emojis por categorías con el botón de la cara sonriente.'="Le chat de l’hôte est temporaire et disponible uniquement pendant une session avec l’autorisation Chat. Il ne comporte ni sélecteur d’appareil ni options d’exportation ou de suppression manuelle. Lorsque l’hôte ou le client termine la session, les messages et le brouillon sont effacés de l’hôte, y compris en cas de perte de connexion. Le chat n’est pas enregistré sur disque. Le client conserve son historique par appareil. Le bouton souriant permet d’insérer des émojis par catégorie.";
  'Si el host está minimizado u oculto y la conexión requiere aprobación, su ventana se restaura para mostrar la solicitud de permisos. Debes aceptar o rechazar; mostrar la ventana no concede permisos automáticamente.'="Si l’hôte est réduit ou masqué et que la connexion nécessite une approbation, sa fenêtre est restaurée pour afficher la demande d’autorisations. Vous devez accepter ou refuser ; afficher la fenêtre ne donne aucune autorisation automatiquement.";
  'Revisa los permisos'='Vérifiez les autorisations';
  'Selecciona los permisos, incluido audio o portapapeles si los necesitas, y pulsa Conectar.'='Sélectionnez les autorisations, y compris le son ou le presse-papiers si nécessaire, puis cliquez sur Connecter.';
  'Al elegir un dispositivo guardado se preparan el ID y la contraseña. Revisa los permisos antes de pulsar Conectar: audio y portapapeles son opcionales y deben solicitarse antes de iniciar la sesión.'="La sélection d’un appareil enregistré préremplit son ID et son mot de passe. Vérifiez les autorisations avant de cliquer sur Connecter : le son et le presse-papiers sont facultatifs et doivent être demandés avant de démarrer la session.";
  'Dispositivo'='Appareil';'Borrar chat'='Effacer le chat';'Exportar chat'='Exporter le chat';'Chat con {0}'='Discussion avec {0}';'Yo'='Moi';
  'Más usados'='Les plus utilisés';'Caras y emociones'='Visages et émotions';'Gestos y personas'='Gestes et personnes';
  'Animales y naturaleza'='Animaux et nature';'Comida y bebida'='Aliments et boissons';'Viajes y lugares'='Voyages et lieux';
  'Actividades'='Activités';'Objetos y símbolos'='Objets et symboles';
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
  'Se borrará el historial de chat de {0}. ¿Continuar?'="S’esborrarà l’historial de xat de {0}. Vols continuar?";
  '¿Quieres eliminar «{0}» ({1}) de tus dispositivos guardados? También se borrará su historial de chat.'="Vols eliminar «{0}» ({1}) dels dispositius desats? També s’esborrarà el seu historial de xat.";
  'Selecciona un dispositivo para consultar solo su conversación, incluso sin conexión. Borrar chat pide confirmación y elimina su historial local. Al eliminar un dispositivo también se borra su chat y desaparece del selector. Exportar chat guarda toda la conversación en un archivo de texto UTF-8. Los emojis están organizados por categorías. Para enviar mensajes necesitas una sesión con permiso Chat.'="Selecciona un dispositiu per consultar només la seva conversa, fins i tot sense connexió. Esborra el xat demana confirmació i elimina el seu historial local. Eliminar un dispositiu també esborra el seu xat i el treu del selector. Exporta el xat desa tota la conversa en un fitxer de text UTF-8. Els emojis estan organitzats per categories. Per enviar missatges necessites una sessió amb permís Xat.";
  'Captura moderna sin respuesta; recuperando la imagen con GDI.'="La captura moderna no respon; recuperant la imatge amb GDI.";
  'Activado (en ejecución)'="Activat (en execució)";
  'Arranca con Windows mediante una tarea programada. Consultar el estado no requiere UAC; cambiar el inicio requiere administración.'="S’inicia amb Windows mitjançant una tasca programada. Consultar l’estat no requereix UAC; canviar l’inici requereix administració.";
  'Falta el agente de arranque: {0}. Repara la instalación de CSIViewer Host.'="Falta l’agent d’inici: {0}. Repara la instal·lació de CSIViewer Host.";
  'No existe el archivo o la carpeta.'='El fitxer o la carpeta no existeix.';
  'El agente debe estar instalado en Program Files.'="L’agent ha d’estar instal·lat a Program Files.";
  'La ruta contiene un enlace o una redirección.'='La ruta conté un enllaç o una redirecció.';
  'El propietario no es SYSTEM, Administradores ni TrustedInstaller.'='El propietari no és SYSTEM, Administradors ni TrustedInstaller.';
  'Una cuenta no administrativa puede modificar la instalación.'='Un compte no administratiu pot modificar la instal·lació.';
  'Windows no permite leer los permisos de esta ruta.'="Windows no permet llegir els permisos d’aquesta ruta.";
  'No se puede activar el inicio. Ruta: {0}. Motivo: {1}'="No es pot activar l’inici. Ruta: {0}. Motiu: {1}";
  'No se pueden comprobar los permisos de {0}. No se ha modificado el inicio automático.'="No es poden comprovar els permisos de {0}. No s’ha modificat l’inici automàtic.";
  'Valores por defecto'='Valors per defecte';'Restablecer'='Desfés els canvis';
  'Iniciar servidor y Crear servicio utilizan la configuración guardada.'='Inicia el servidor i Crea el servei utilitzen la configuració desada.';
  'Estado no accesible. Actualiza con permisos de administrador.'="Estat no accessible. Actualitza amb permisos d’administrador.";
  ' (último estado confirmado)'=' (últim estat confirmat)';
  'Para activar el inicio, instala CSIViewer Host para todos los usuarios en una carpeta protegida.'="Per activar l’inici, instal·la CSIViewer Host per a tots els usuaris en una carpeta protegida.";
  'Permiso de administrador cancelado; no se ha cambiado nada.'="Permís d’administrador cancel·lat; no s’ha canviat res.";
  'La operación no se ha confirmado. Código {0}. No se mostrará como activada sin verificar la tarea.'="L’operació no s’ha confirmat. Codi {0}. No es mostrarà com a activada sense verificar la tasca.";
  'Acceso al inicio de Windows'="Accés a l’inici de sessió de Windows";
  'Requiere administración y un ID de equipo distinto. HTTP remoto solo con autorización explícita.'="Requereix administració i un ID d’equip diferent. HTTP remot només amb autorització explícita.";
  'Instala CSIViewer Host para todos los usuarios en la ruta predeterminada para configurar esta función.'='Instal·la CSIViewer Host per a tots els usuaris a la ubicació predeterminada per configurar aquesta funció.';
  'Conectado sin control de ratón y teclado'='Connectat sense control del ratolí i del teclat';
  'Otra sesión tiene el control de este equipo. Finaliza esa sesión y vuelve a conectar solicitando el permiso de control.'="Una altra sessió té el control d’aquest equip. Finalitza aquesta sessió i torna a connectar sol·licitant el permís de control.";
  'El equipo remoto no ha concedido el permiso de control. Finaliza la sesión y vuelve a solicitar «Controlar ratón y teclado».'="L’equip remot no ha concedit el permís de control. Finalitza la sessió i torna a sol·licitar «Controlar el ratolí i el teclat».";
  'Vista de {0} (sin control)'='Vista de {0} (sense control)';
  'Sesión con {0}'='Sessió amb {0}';
  'Equipos vinculados'='Equips vinculats';
  'En la ficha puedes añadir empresa, correo, teléfono y notas. Los favoritos aparecen primero. Abre Dispositivos vinculados para buscar y seleccionar varios equipos, o desvincularlos. Cancelar descarta los cambios; Guardar conserva los datos y los vínculos.'="A la fitxa pots afegir empresa, correu, telèfon i notes. Els preferits apareixen primer. Obre Dispositius vinculats per cercar i seleccionar diversos equips o desvincular-los. Cancel·la descarta els canvis; Desa conserva les dades i els vincles.";
  'Empresa'='Empresa';'Correo electrónico'='Correu electrònic';'Teléfono'='Telèfon';'Favorito'='Preferit';
  'Marcar como favorito'='Marca com a preferit';'Dispositivo no guardado ({0})'='Dispositiu no desat ({0})';
  'Seleccionar dispositivos…'='Selecciona dispositius…';'1 dispositivo seleccionado'='1 dispositiu seleccionat';
  '{0} dispositivos seleccionados'='{0} dispositius seleccionats';'Buscar por nombre o ID…'='Cerca per nom o ID…';
  'Puedes seleccionar varios equipos.'='Pots seleccionar diversos equips.';'Desvincular todos'="Desvincula’ls tots";
  'Completa la ficha y vincula los equipos del contacto.'='Completa la fitxa i vincula els equips del contacte.';
  'Introduce un nombre para el contacto.'='Introdueix un nom per al contacte.';'Revisa el correo electrónico.'='Revisa el correu electrònic.';
  'Conexión en curso'='Connexió en curs';'Listo para conectar'='A punt per connectar';
  'Introduce el ID, revisa los permisos y pulsa Conectar.'="Introdueix l’ID, revisa els permisos i prem Connecta.";
  'Esperando aceptación del equipo remoto…'="Esperant l’acceptació de l’equip remot…";
  'Conectando con el equipo remoto…'="Connectant amb l’equip remot…";
  'El equipo remoto está validando la conexión y los permisos solicitados. Espera a que la autorice.'="L’equip remot està validant la connexió i els permisos sol·licitats. Espera que l’autoritzi.";
  'Enviando la solicitud y estableciendo una conexión segura. Puede tardar unos instantes.'="Enviant la sol·licitud i establint una connexió segura. Pot trigar uns instants.";
  'El chat del host es temporal y solo está disponible durante una sesión con permiso Chat. No tiene selector de dispositivo ni opciones de exportación o borrado manual. Cuando el host o el visor finaliza la sesión, los mensajes y el borrador se eliminan del host; también se vacían si se pierde la conexión. No se guarda el chat en disco. El visor conserva su historial por dispositivo. Puedes insertar emojis por categorías con el botón de la cara sonriente.'="El xat de l’amfitrió és temporal i només està disponible durant una sessió amb permís Xat. No té selector de dispositiu ni opcions d’exportació o esborrament manual. Quan l’amfitrió o el visor finalitza la sessió, els missatges i l’esborrany s’eliminen de l’amfitrió; també es buiden si es perd la connexió. El xat no es desa al disc. El visor conserva l’historial per dispositiu. Pots inserir emojis per categories amb el botó de la cara somrient.";
  'Si el host está minimizado u oculto y la conexión requiere aprobación, su ventana se restaura para mostrar la solicitud de permisos. Debes aceptar o rechazar; mostrar la ventana no concede permisos automáticamente.'="Si l’amfitrió està minimitzat o ocult i la connexió requereix aprovació, la finestra es restaura per mostrar la sol·licitud de permisos. Has d’acceptar o rebutjar; mostrar la finestra no concedeix permisos automàticament.";
  'Revisa los permisos'='Revisa els permisos';
  'Selecciona los permisos, incluido audio o portapapeles si los necesitas, y pulsa Conectar.'='Selecciona els permisos, incloent-hi àudio o porta-retalls si els necessites, i prem Connecta.';
  'Al elegir un dispositivo guardado se preparan el ID y la contraseña. Revisa los permisos antes de pulsar Conectar: audio y portapapeles son opcionales y deben solicitarse antes de iniciar la sesión.'="En seleccionar un dispositiu desat es preparen l’ID i la contrasenya. Revisa els permisos abans de prémer Connecta: l’àudio i el porta-retalls són opcionals i s’han de sol·licitar abans d’iniciar la sessió.";
  'Dispositivo'='Dispositiu';'Borrar chat'='Esborra el xat';'Exportar chat'='Exporta el xat';'Chat con {0}'='Xat amb {0}';'Yo'='Jo';
  'Más usados'='Més utilitzats';'Caras y emociones'='Cares i emocions';'Gestos y personas'='Gestos i persones';
  'Animales y naturaleza'='Animals i natura';'Comida y bebida'='Menjar i beguda';'Viajes y lugares'='Viatges i llocs';
  'Actividades'='Activitats';'Objetos y símbolos'='Objectes i símbols';
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
$spanishPath=Join-Path $Root 'src/CSIViewer.Core/Resources/es.json'
$spanish=[Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($spanishPath))
$activeKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($property in $spanish.RootElement.EnumerateObject()){[void]$activeKeys.Add($property.Name)}
$spanish.Dispose()
foreach($language in @('en','fr','ca')){
 $path=Join-Path $Root "src/CSIViewer.Core/Resources/$language.json"
 $document=[Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($path))
 $values=[Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
  foreach($property in $document.RootElement.EnumerateObject()){
   if($activeKeys.Contains($property.Name)){$values[$property.Name]=$property.Value.GetString()}
  }
 $document.Dispose()
  foreach($key in $overrides[$language].Keys){
   if($activeKeys.Contains($key)){$values[$key]=$overrides[$language][$key]}
  }
 foreach($key in @($values.Keys)){
   $leading=[regex]::Match($key,'^\s*').Value;$trailing=[regex]::Match($key,'\s*$').Value
   $values[$key]=$leading+$values[$key].Trim()+$trailing
 }
 [IO.File]::WriteAllText($path,[Text.Json.JsonSerializer]::Serialize($values,$options))
}
