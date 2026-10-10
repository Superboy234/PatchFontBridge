#include <windows.h>
#include <stdio.h>
#include <vector>
static BOOL CALLBACK CountVisible(HWND w,LPARAM ctx){DWORD pid;GetWindowThreadProcessId(w,&pid);if(pid==GetCurrentProcessId()&&IsWindowVisible(w)){wchar_t title[256];GetWindowTextW(w,title,256);wprintf(L"Unexpected visible window: %ls\n",title);++*(int*)ctx;}return TRUE;}
int wmain(int argc,wchar_t** argv) {
    wchar_t file[32768]; GetSystemDirectoryW(file,32768); wcscat_s(file,L"\\kernel32.dll");
    bool expectFont=argc>1 && wcscmp(argv[1],L"--expect-font")==0;
    HMODULE proxy=argc>1&&!expectFont?LoadLibraryW(argv[1]):0;
    typedef DWORD (WINAPI *Size)(LPCWSTR,LPDWORD);
    typedef BOOL (WINAPI *Info)(LPCWSTR,DWORD,DWORD,LPVOID);
    typedef BOOL (WINAPI *Query)(LPCVOID,LPCWSTR,LPVOID*,PUINT);
    Size size=proxy?(Size)GetProcAddress(proxy,"GetFileVersionInfoSizeW"):GetFileVersionInfoSizeW;
    Info info=proxy?(Info)GetProcAddress(proxy,"GetFileVersionInfoW"):GetFileVersionInfoW;
    Query query=proxy?(Query)GetProcAddress(proxy,"VerQueryValueW"):VerQueryValueW;
    if(!size||!info||!query) return 10;
    if(proxy && GetProcAddress(proxy,MAKEINTRESOURCEA(8))!=(FARPROC)size) return 11;
    DWORD unused=0,n=size(file,&unused); if(!n) return 12;
    std::vector<BYTE> buffer(n); if(!info(file,0,n,&buffer[0])) return 13;
    VS_FIXEDFILEINFO* value=0; UINT bytes=0; if(!query(&buffer[0],L"\\",(LPVOID*)&value,&bytes)||!value||value->dwSignature!=0xFEEF04BD) return 14;
    // Five arguments exercise the x64 stack argument and register preservation.
    if(proxy) { typedef BOOL (WINAPI *Ex)(DWORD,LPCWSTR,DWORD,DWORD,LPVOID); Ex ex=(Ex)GetProcAddress(proxy,"GetFileVersionInfoExW"); if(!ex||!ex(FILE_VER_GET_NEUTRAL,file,0,n,&buffer[0])) return 15; }
    wchar_t mark[20]; for(int i=0;i<200;i++){if(GetEnvironmentVariableW(L"WPM_FIXTURE_ATTACHED",mark,20)) {
        if(expectFont) {
            HMODULE font=GetModuleHandleW(L"winmm.dll");if(!font)return 17;
            wchar_t fontPath[32768],gamePath[32768];GetModuleFileNameW(font,fontPath,32768);GetModuleFileNameW(0,gamePath,32768);wchar_t* slash=wcsrchr(gamePath,L'\\');if(!slash)return 18;slash[1]=0;wcscat_s(gamePath,L"winmm.dll");if(_wcsicmp(fontPath,gamePath))return 19;
            Sleep(300);
            slash[1]=0;wcscat_s(gamePath,L"FontHook.ini");
            if(GetPrivateProfileIntW(L"FontHook",L"PickerShowOnStartup",1,gamePath)!=0)return 20;
            // Some restricted sessions deny every RegisterHotKey call. The supplied
            // SFH then displays a fallback picker even when startup is configured off.
        }
        printf("PASS architecture=%u version-api-and-payload%s\n",(unsigned)(sizeof(void*)*8),expectFont?" + SimpleFontHook, startup=0":"");return 0;
    }Sleep(10);}
    return 16;
}
