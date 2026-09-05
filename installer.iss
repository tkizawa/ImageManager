#ifndef MyAppVersion
#define MyAppVersion "1.1.4.0"
#endif
#ifndef MyOutputDir
#define MyOutputDir "Installer"
#endif
#ifndef MyArch
#define MyArch "x64"
#endif
#ifndef MyOutputFilename
#define MyOutputFilename "ImageManager_Setup_" + MyAppVersion + "_" + MyArch
#endif

[Setup]
AppName=WoodStream ImageManager
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ImageManager
DefaultGroupName=WoodStream ImageManager
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputFilename}
Compression=lzma2/ultra64
SolidCompression=yes
#if MyArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
SetupIconFile=Assets\AppIcon.ico
PrivilegesRequired=admin
DisableWelcomePage=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "bin\Release\net10.0-windows10.0.19041.0\win-{#MyArch}\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WoodStream ImageManager"; Filename: "{app}\ImageManager.exe"
Name: "{autodesktop}\WoodStream ImageManager"; Filename: "{app}\ImageManager.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

