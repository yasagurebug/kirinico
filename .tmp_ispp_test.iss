#define CsprojPath AddBackslash(SourcePath) + "Kirinico.App\\Kirinico.App.csproj"
#define H FileOpen(CsprojPath)
#pragma message "PATH=" + CsprojPath
#pragma message "HANDLE=" + Str(H)
#if H
  #define L FileRead(H)
  #pragma message "LINE1=" + L
  #expr FileClose(H)
#endif
[Setup]
AppName=Test
AppVersion=1.0
DefaultDirName={tmp}\t
