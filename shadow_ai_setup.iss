; Shadow AI Inno Setup Script
; Installs Shadow AI and verifies/installs .NET 8.0 Windows Desktop Runtime using winget.

[Setup]
AppName=Shadow AI
AppVersion=1.0
AppPublisher=Shadow AI
AppPublisherURL=https://shadowai.com
DefaultDirName={autopf}\ShadowAI
DefaultGroupName=Shadow AI
OutputDir=d:\uday\projects\WebSites\Overlays\Website
OutputBaseFilename=setup
SetupIconFile=d:\uday\projects\WebSites\Overlays\OverlayApp\shadow_ai.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\SystemCoreHost.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\D3DCompiler_47_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\PenImc_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\PresentationNative_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\vcruntime140_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\wpfgfx_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "d:\uday\projects\WebSites\Overlays\OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\ocr.py"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Shadow AI"; Filename: "{app}\SystemCoreHost.exe"
Name: "{autodesktop}\Shadow AI"; Filename: "{app}\SystemCoreHost.exe"; Tasks: desktopicon

[Run]
; Check and install .NET Desktop Runtime using winget
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""winget install Microsoft.DotNet.DesktopRuntime.8 --silent --accept-source-agreements --accept-package-agreements"""; StatusMsg: "Verifying and installing Microsoft .NET Desktop Runtime dependency..."; Flags: runhidden; Check: CheckDotNetNeeded
Filename: "{app}\SystemCoreHost.exe"; Description: "{cm:LaunchProgram,Shadow AI}"; Flags: nowait postinstall skipifsilent

[Code]
function CheckDotNetNeeded: Boolean;
var
  Cmd, Args, TempFile: string;
  OutputString: AnsiString;
  ResultCode: Integer;
begin
  // Default to true (i.e. check and install if not found)
  Result := True;
  
  // Use dotnet --list-runtimes to check if Microsoft.WindowsDesktop.App 8.x or 10.x is installed
  TempFile := ExpandConstant('{tmp}\dotnet_check.txt');
  Cmd := ExpandConstant('{cmd}');
  Args := '/C dotnet --list-runtimes > "' + TempFile + '" 2>&1';
  
  if Exec(Cmd, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    if LoadStringFromFile(TempFile, OutputString) then
    begin
      // If Microsoft.WindowsDesktop.App 8. or 10. is in the list, we skip winget
      if (Pos('Microsoft.WindowsDesktop.App 8.', OutputString) > 0) or 
         (Pos('Microsoft.WindowsDesktop.App 10.', OutputString) > 0) then
      begin
        Result := False;
      end;
    end;
  end;
  DeleteFile(TempFile);
end;
