#define CsprojPath AddBackslash(SourcePath) + "Kirinico.App\\Kirinico.App.csproj"
#define H 0
#define L ""
#define AppVersion ""
#sub ReadLine
  #define L = FileRead(H)
  #pragma message "L=" + L
  #if Pos("<Version>", L) > 0 && Pos("</Version>", L) > Pos("<Version>", L)
    #define AppVersion = Copy(L, Pos("<Version>", L) + Len("<Version>"), Pos("</Version>", L) - (Pos("<Version>", L) + Len("<Version>")))
    #pragma message "FOUND=" + AppVersion
  #endif
#endsub
#for {H = FileOpen(CsprojPath); H && !FileEof(H) && AppVersion == ""; ""} ReadLine
#if H
  #expr FileClose(H)
#endif
#pragma message "FINAL=" + AppVersion
[Setup]
AppName=Test
AppVersion=1.0
DefaultDirName={tmp}\t
