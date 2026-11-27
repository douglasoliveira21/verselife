; Windows installer for the LifeVerse client (the Unity build). Called by scripts/build-unity-client.ps1 -Installer:
;   iscc /DAppVersion=0.7.0 /DEdition=-unity /DSourceDir=<folder with the build> installer\LifeVerse.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Edition
  #define Edition ""
#endif
#ifndef SourceDir
  #define SourceDir "..\build\windows"
#endif

[Setup]
AppId={{6F1C2B7E-5D0A-4E7B-9C3A-1B2F4D8E9A10}
AppName=LifeVerse
AppVersion={#AppVersion}
AppPublisher=LifeVerse
DefaultDirName={autopf}\LifeVerse
DefaultGroupName=LifeVerse
UninstallDisplayIcon={app}\LifeVerse.exe
SetupIconFile=..\assets\brand\icon.ico
OutputDir=..\build\installer
OutputBaseFilename=LifeVerse-Setup-{#AppVersion}{#Edition}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; admin: the installer adds the rule that lets the game through the Windows Firewall (it cannot be added without it)
PrivilegesRequired=admin
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\LifeVerse"; Filename: "{app}\LifeVerse.exe"
Name: "{autodesktop}\LifeVerse"; Filename: "{app}\LifeVerse.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; Flags: unchecked

[Run]
; Windows Firewall: a rule for this program, so the system does not ask on the first run (an old rule of the same name goes first, so there are never two)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LifeVerse"" program=""{app}\LifeVerse.exe"""; Flags: runhidden; StatusMsg: "Liberando o LifeVerse no Firewall do Windows..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LifeVerse"" dir=in action=allow program=""{app}\LifeVerse.exe"" enable=yes profile=any"; Flags: runhidden; StatusMsg: "Liberando o LifeVerse no Firewall do Windows..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LifeVerse"" dir=out action=allow program=""{app}\LifeVerse.exe"" enable=yes profile=any"; Flags: runhidden
Filename: "{app}\LifeVerse.exe"; Description: "Abrir o LifeVerse"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LifeVerse"" program=""{app}\LifeVerse.exe"""; Flags: runhidden; RunOnceId: "LifeVerseFirewall"
