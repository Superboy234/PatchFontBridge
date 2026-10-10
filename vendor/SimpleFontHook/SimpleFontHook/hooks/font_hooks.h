#pragma once
#include "../framework.h"

namespace FontHooks {
    void Install(HMODULE hModule);
    void PrepareForProcessExit();
    HFONT CreateProbeFont(const LOGFONTW* font);
}
