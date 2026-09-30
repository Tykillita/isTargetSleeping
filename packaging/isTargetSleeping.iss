; Instalador por usuario de isTargetSleeping (Inno Setup 6). Lo compila package.ps1:
;   ISCC /DAppVersion=1.0.0 /DAppArch=x64 /DSourceExe=build\isTargetSleeping.exe packaging\isTargetSleeping.iss
; Sin administrador: se instala en %LOCALAPPDATA%\Programs\isTargetSleeping, como Ollama.

#define AppName "isTargetSleeping"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef SourceExe
  #define SourceExe "..\build\" + AppName + ".exe"
#endif

[Setup]
AppId={{9E4F2B71-3C8A-4D56-B0E2-7A1C5F9D3E48}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CodeSentry - Tykillita
AppPublisherURL=https://github.com/Tykillita/isTargetSleeping
AppSupportURL=https://github.com/Tykillita/isTargetSleeping/issues
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename={#AppName}-{#AppVersion}-setup-{#AppArch}
SetupIconFile=..\Assets\{#AppName}.ico
UninstallDisplayIcon={app}\{#AppName}.exe
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
#if AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppName}.exe"

[Run]
Filename: "{app}\{#AppName}.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppName}.exe /F"; Flags: runhidden; RunOnceId: "StopApp"
; El agente de memoria (si se activó «Liberar RAM») vive en Program Files con una tarea programada:
; quitarlo pide permiso de administrador (UAC).
Filename: "{commonpf64}\{#AppName}\cleaner\{#AppName}.MemoryAgent.exe"; Parameters: "--uninstall"; Verb: "runas"; Flags: shellexec waituntilterminated skipifdoesntexist; RunOnceId: "RemoveMemoryAgent"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\{#AppName}"

[Registry]
; La app decide si abrir al iniciar sesión (lo pregunta la primera vez); al
; desinstalar se borra la entrada si existe.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey
; Los enlaces istargetsleeping:// los registra la app al arrancar; al desinstalar se borran.
Root: HKCU; Subkey: "Software\Classes\istargetsleeping"; ValueType: none; Flags: uninsdeletekey dontcreatekey
