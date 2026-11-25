; Instalador de MojarraTube (NSIS, por usuario: no pide permisos de administrador)
; Compilar: scripts/compilar.sh (Linux/WSL) o scripts/compilar.ps1 (Windows)
Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define NOMBRE "MojarraTube"
!define EXE "MojarraTube.exe"
!define UNKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NOMBRE}"
!define APPKEY "Software\Classes\Applications\${EXE}"

Name "${NOMBRE}"
Caption "Instalar ${NOMBRE}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${NOMBRE}"
BrandingText "Mojarrium"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${NOMBRE}"
VIAddVersionKey "FileDescription" "Instalador de ${NOMBRE}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "Mojarrium"
VIAddVersionKey "LegalCopyright" "Mojarrium"

!include "MUI2.nsh"
!define MUI_ICON "..\build\icon.ico"
!define MUI_UNICON "..\build\icon.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Bienvenido a ${NOMBRE}"
!define MUI_WELCOMEPAGE_TEXT "Videos de YouTube y de cualquier página, descargas en la calidad que quieras o solo el audio, y un reproductor propio.$\r$\n$\r$\nSe instalará solo para tu usuario y no necesita permisos de administrador.$\r$\n$\r$\nLa primera vez que lo abras descargará sus componentes (yt-dlp, Deno y FFmpeg, unos 150 MB)."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir ${NOMBRE}"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Spanish"

!macro Tipo EXT
  WriteRegStr HKCU "${APPKEY}\SupportedTypes" "${EXT}" ""
  WriteRegStr HKCU "Software\Classes\${EXT}\OpenWithList\${EXE}" "" ""
!macroend

Section "Instalar"
  ; Si está abierto, se cierra para poder actualizarlo
  nsExec::Exec 'taskkill /IM "${EXE}" /F'
  Sleep 400
  ; Se borra la versión anterior del programa (nunca los datos de Documentos\MojarraTube)
  RMDir /r "$INSTDIR\libvlc"
  SetOutPath "$INSTDIR"
  File /r "${SRC}\*.*"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"

  CreateShortcut "$SMPROGRAMS\${NOMBRE}.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$DESKTOP\${NOMBRE}.lnk" "$INSTDIR\${EXE}"

  WriteRegStr HKCU "Software\${NOMBRE}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNKEY}" "DisplayName" "${NOMBRE}"
  WriteRegStr HKCU "${UNKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNKEY}" "Publisher" "Mojarrium"
  WriteRegStr HKCU "${UNKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNKEY}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegDWORD HKCU "${UNKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNKEY}" "NoRepair" 1

  ; "Abrir con… MojarraTube" para videos y audios
  WriteRegStr HKCU "${APPKEY}" "FriendlyAppName" "${NOMBRE}"
  WriteRegStr HKCU "${APPKEY}\DefaultIcon" "" "$INSTDIR\${EXE},0"
  WriteRegStr HKCU "${APPKEY}\shell\open" "FriendlyAppName" "${NOMBRE}"
  WriteRegStr HKCU "${APPKEY}\shell\open\command" "" '"$INSTDIR\${EXE}" "%1"'
  !insertmacro Tipo ".mp4"
  !insertmacro Tipo ".mkv"
  !insertmacro Tipo ".webm"
  !insertmacro Tipo ".mov"
  !insertmacro Tipo ".avi"
  !insertmacro Tipo ".m4v"
  !insertmacro Tipo ".wmv"
  !insertmacro Tipo ".mp3"
  !insertmacro Tipo ".m4a"
  !insertmacro Tipo ".opus"
  !insertmacro Tipo ".flac"
  !insertmacro Tipo ".wav"
  !insertmacro Tipo ".ogg"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

!macro QuitarTipo EXT
  DeleteRegKey HKCU "Software\Classes\${EXT}\OpenWithList\${EXE}"
!macroend

Section "Uninstall"
  nsExec::Exec 'taskkill /IM "${EXE}" /F'
  Sleep 400
  Delete "$SMPROGRAMS\${NOMBRE}.lnk"
  Delete "$DESKTOP\${NOMBRE}.lnk"
  RMDir /r "$INSTDIR"
  ; Componentes descargados y caché de miniaturas
  RMDir /r "$LOCALAPPDATA\${NOMBRE}"
  DeleteRegKey HKCU "${UNKEY}"
  DeleteRegKey HKCU "Software\${NOMBRE}"
  DeleteRegKey HKCU "${APPKEY}"
  !insertmacro QuitarTipo ".mp4"
  !insertmacro QuitarTipo ".mkv"
  !insertmacro QuitarTipo ".webm"
  !insertmacro QuitarTipo ".mov"
  !insertmacro QuitarTipo ".avi"
  !insertmacro QuitarTipo ".m4v"
  !insertmacro QuitarTipo ".wmv"
  !insertmacro QuitarTipo ".mp3"
  !insertmacro QuitarTipo ".m4a"
  !insertmacro QuitarTipo ".opus"
  !insertmacro QuitarTipo ".flac"
  !insertmacro QuitarTipo ".wav"
  !insertmacro QuitarTipo ".ogg"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  ; Tu historial, suscripciones y descargas se conservan (Documentos\MojarraTube, Videos\MojarraTube)
SectionEnd
