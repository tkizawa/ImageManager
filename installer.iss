[Setup]
AppName=ImageManager
AppVersion=1.0.0.2
DefaultDirName={autopf}\ImageManager
DefaultGroupName=ImageManager
OutputDir=Output
OutputBaseFilename=ImageManager_Setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=Assets\AppIcon.ico
PrivilegesRequired=admin
DisableWelcomePage=no

[Files]
Source: "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ImageManager"; Filename: "{app}\ImageManager.exe"
Name: "{autodesktop}\ImageManager"; Filename: "{app}\ImageManager.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
