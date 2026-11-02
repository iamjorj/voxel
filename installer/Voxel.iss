; Voxel installer (Inno Setup 6). Built by installer/build.ps1, which publishes the app first.
; Per-user install (no admin prompt): Voxel asks for admin itself only when it installs VB-CABLE.

#define AppVersion GetEnv("VOXEL_VERSION")
#if AppVersion == ""
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6F1C2A7E-3B9D-4E52-9A41-7C6CF6B0E10D}
AppName=Voxel
AppVersion={#AppVersion}
AppVerName=Voxel {#AppVersion}
AppPublisher=Javohir Doniyorov
AppPublisherURL=https://github.com/iamjorj/voxel
AppSupportURL=https://github.com/iamjorj/voxel/issues
DefaultDirName={autopf}\Voxel
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=VoxelSetup-{#AppVersion}
SetupIconFile=..\Voxel\Assets\voxel.ico
UninstallDisplayIcon={app}\Voxel.exe
WizardStyle=modern
; lzma2/ultra64 runs the compiler out of memory on the ~200 MB of AI models
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
LicenseFile=..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Voxel"; Filename: "{app}\Voxel.exe"
Name: "{autodesktop}\Voxel"; Filename: "{app}\Voxel.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Voxel.exe"; Description: "{cm:LaunchProgram,Voxel}"; Flags: nowait postinstall skipifsilent
