#define MyAppName "ECLIPSE"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ECLIPSE"
#define MyAppExeName "AplicativoAnalizador.exe"

[Setup]
AppId={{B3F1C2A4-5D7E-4F10-9A21-6C8E2D4B7A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=Setup_ECLIPSE_v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "iniciowindows"; Description: "Iniciar ECLIPSE junto con Windows"; GroupDescription: "Opciones:"

[Files]
Source: "C:\Users\PRIMER\Desktop\APLICATIVO ANALIZADOR\AplicativoAnalizador\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ECLIPSE"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: iniciowindows; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar ECLIPSE"; Flags: nowait postinstall skipifsilent