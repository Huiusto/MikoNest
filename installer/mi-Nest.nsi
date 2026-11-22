; MikoNest 安装程序脚本（NSIS 3）
Unicode true
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"

; ---------------- 编译期路径 ----------------
!define ROOT "C:\Users\35847\Doubao\chats\2026-10-13\new-chat\mi-Nest"
!define ICON_PATH "${ROOT}\src\mi-Nest\Assets\logo.ico"
!define PUBLISH_DIR "${ROOT}\publish"
!define ART_DIR "${ROOT}\installer\assets"

; ---------------- 基本设置 ----------------
Name "MikoNest"
OutFile "C:\Users\35847\Doubao\chats\2026-10-13\new-chat\MikoNest-Setup-0.1.28.exe"
InstallDir "$PROGRAMFILES64\MikoNest"
InstallDirRegKey HKLM "Software\MikoNest" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!define APP_NAME "MikoNest"
!define APP_VERSION "0.1.28"
!define APP_PUBLISHER "MikoNest Contributors"

VIProductVersion "0.1.28.0"
VIAddVersionKey "ProductName" "MikoNest"
VIAddVersionKey "FileDescription" "MikoNest 安装程序"
VIAddVersionKey "FileVersion" "0.1.28"
VIAddVersionKey "ProductVersion" "0.1.28"
VIAddVersionKey "LegalCopyright" "MikoNest Contributors"

; ---------------- 图标 ----------------
Icon "${ICON_PATH}"
UninstallIcon "${ICON_PATH}"

; ---------------- 主题 ----------------
!define MUI_ABORTWARNING
!define MUI_ICON "${ICON_PATH}"
!define MUI_UNICON "${ICON_PATH}"

!define MUI_BGCOLOR "FDF3ED"
!define MUI_TEXTCOLOR "8A4B44"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${ART_DIR}\finish.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP_STRETCH FitControl

!define MUI_FINISHPAGE_RUN "$INSTDIR\MikoNest.exe"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 MikoNest"
!define MUI_FINISHPAGE_LINK "访问项目主页（GitHub）"
!define MUI_FINISHPAGE_LINK_LOCATION "https://github.com/Huiusto/mi-Nest"

; ---------------- 向导页 ----------------
Page custom WelcomeCreate WelcomeLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

; ---------------- 首屏：整窗满幅背景图 ----------------
Var hWelcomePage
Var hWelcomeImg
Var hWelcomeBmp

Function .onInit
  InitPluginsDir
  File /oname=$PLUGINSDIR\welcome.bmp "${ART_DIR}\welcome.bmp"
FunctionEnd

Function WelcomeCreate
  ; 1044 = MUI 全窗口页面模板（无头图带、无页边距）
  nsDialogs::Create 1044
  Pop $hWelcomePage
  ${If} $hWelcomePage == error
    Abort
  ${EndIf}
  SetCtlColors $hWelcomePage "" "FDF3ED"

  Call muiPageLoadFullWindow

  ; 背景图铺满整个窗体内容区
  ${NSD_CreateBitmap} 0 0 100% 100% ""
  Pop $hWelcomeImg
  ${NSD_SetStretchedImage} $hWelcomeImg "$PLUGINSDIR\welcome.bmp" $hWelcomeBmp

  ; 首屏按钮文案：下一步 → 开始安装
  GetDlgItem $1 $HWNDPARENT 1
  SendMessage $1 ${WM_SETTEXT} 0 "STR:开始安装"

  nsDialogs::Show
  Call muiPageUnloadFullWindow
FunctionEnd

Function WelcomeLeave
FunctionEnd

; ---------------- 安装 ----------------
Section "主程序" SecMain
  ; 清理旧版（HoYoNest、mi-Nest）残留：结束旧进程、快捷方式、注册表、旧安装目录
  nsExec::Exec 'taskkill /IM mi-Nest.exe /F'
  nsExec::Exec 'taskkill /IM MikoNest.exe /F'
  Sleep 900

  Delete "$DESKTOP\HoYoNest.lnk"
  Delete "$SMPROGRAMS\HoYoNest\HoYoNest.lnk"
  Delete "$SMPROGRAMS\HoYoNest\卸载 HoYoNest.lnk"
  RMDir "$SMPROGRAMS\HoYoNest"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\HoYoNest"
  DeleteRegKey HKLM "Software\HoYoNest"
  IfFileExists "$PROGRAMFILES64\HoYoNest\HoYoNest.exe" 0 +2
    RMDir /r "$PROGRAMFILES64\HoYoNest"

  Delete "$DESKTOP\mi-Nest.lnk"
  Delete "$SMPROGRAMS\mi-Nest\mi-Nest.lnk"
  Delete "$SMPROGRAMS\mi-Nest\卸载 mi-Nest.lnk"
  RMDir "$SMPROGRAMS\mi-Nest"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\mi-Nest"
  DeleteRegKey HKLM "Software\mi-Nest"
  IfFileExists "$PROGRAMFILES64\mi-Nest\mi-Nest.exe" 0 +2
    RMDir /r "$PROGRAMFILES64\mi-Nest"

  SetOutPath "$INSTDIR"
  ; 应用内「一键更新」会静默运行本安装包：先结束正在运行的旧进程，避免文件占用。
  ; 安装包自身的文件名是 MikoNest-Setup-*.exe，不会被这条命令误杀。
  ; 进程退出后文件可能仍被系统占用，重试删除几次再覆盖
  StrCpy $0 0
  ${Do}
    ClearErrors
    Delete "$INSTDIR\MikoNest.exe"
    ${IfNot} ${Errors}
      ${ExitDo}
    ${EndIf}
    Sleep 600
    IntOp $0 $0 + 1
  ${LoopUntil} $0 >= 6
  File /r "${PUBLISH_DIR}\*.*"

  ; 安装阶段把当前启动页的全部官方动态壁纸缓存到用户数据目录，
  ; 第一次进入启动页即可直接播放，不再显示「正在准备动态壁纸」。
  DetailPrint "正在预加载启动页动态壁纸（可离线跳过）..."
  nsExec::ExecToLog '"$INSTDIR\MikoNest.exe" --prefetch-wallpapers'
  Pop $0

  ; 注册表卸载项（控制面板可卸载）
  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" "$INSTDIR\MikoNest.exe,0"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoModify" "1"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoRepair" "1"
  WriteRegStr HKLM "Software\MikoNest" "InstallDir" "$INSTDIR"

  ; 快捷方式
  CreateShortCut "$DESKTOP\MikoNest.lnk" "$INSTDIR\MikoNest.exe"
  CreateDirectory "$SMPROGRAMS\MikoNest"
  CreateShortCut "$SMPROGRAMS\MikoNest\MikoNest.lnk" "$INSTDIR\MikoNest.exe"
  CreateShortCut "$SMPROGRAMS\MikoNest\卸载 MikoNest.lnk" "$INSTDIR\uninstall.exe"

  ; 应用内「一键更新」是静默安装：装完直接把新版本拉起来
  ${If} ${Silent}
    Exec '"$INSTDIR\MikoNest.exe"'
  ${EndIf}
SectionEnd

; ---------------- 卸载 ----------------
Section "Uninstall"
  Delete "$DESKTOP\MikoNest.lnk"
  Delete "$SMPROGRAMS\MikoNest\MikoNest.lnk"
  Delete "$SMPROGRAMS\MikoNest\卸载 MikoNest.lnk"
  RMDir "$SMPROGRAMS\MikoNest"

  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"

  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
  DeleteRegKey HKLM "Software\MikoNest"
  ; 用户数据（Roaming\MikoNest）保留，不删除
SectionEnd




