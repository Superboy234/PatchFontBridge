#include <windows.h>
BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)SetEnvironmentVariableW(L"WPM_FIXTURE_ATTACHED",L"yes");return TRUE;}
