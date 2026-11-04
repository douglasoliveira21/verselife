; Windows installer for the exported LifeVerse client. Built in CI by .github/workflows/release.yml:
;   iscc /DAppVersion=0.6.0 /DSourceDir=..\build\windows installer\lifeverse.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
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
SetupIconFile=..\client\Assets\brand\icon.ico
OutputDir=..\build\installer
OutputBaseFilename=LifeVerse-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\LifeVerse"; Filename: "{app}\LifeVerse.exe"
Name: "{autodesktop}\LifeVerse"; Filename: "{app}\LifeVerse.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; Flags: unchecked

[Run]
Filename: "{app}\LifeVerse.exe"; Description: "Abrir o LifeVerse"; Flags: nowait postinstall skipifsilent
