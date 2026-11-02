#define AppName "LifeVerse"
#define AppVersion "0.6.0"
[Setup]
AppId={{D7FBBF89-2F5B-4B52-9FB7-8A7F5C38A9F1}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\LifeVerse
DefaultGroupName=LifeVerse
OutputDir=..\artifacts\installer
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\LifeVerse.exe
[Files]
Source: "..\artifacts\release\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{autodesktop}\LifeVerse"; Filename: "{app}\LifeVerse.exe"
Name: "{group}\LifeVerse"; Filename: "{app}\LifeVerse.exe"
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\LifeVerse\cache"
