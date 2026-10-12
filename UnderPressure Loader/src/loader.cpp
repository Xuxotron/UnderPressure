// Updated: 2026-10-12
// Change: Core DLLs preload automatically before all [Mods] entries.
// UnderPressure Loader for TPH.exe (Windows x64, Unity Mono).
// Native WinHTTP proxy: based on TPM-Mod-Loader architecture, with author's permission.
// Original independent mod loader: no fixed mod assembly filenames.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>
#include "exports.h"

namespace {
    HMODULE g_self = nullptr;
    HMODULE g_realWinHttp = nullptr;
    FARPROC g_exports[kExportCount] = {};
    INIT_ONCE g_realOnce = INIT_ONCE_STATIC_INIT;
    HHOOK g_hook = nullptr;
    HWND g_gameWindow = nullptr;
    volatile LONG g_startState = 0; // 0=waiting, 1=running, 2=finished, 3=error
    wchar_t g_baseDir[MAX_PATH] = {};
    wchar_t g_modsDir[MAX_PATH] = {};
    wchar_t g_coreDir[MAX_PATH] = {};
    wchar_t g_logPath[MAX_PATH] = {};
    std::vector<std::wstring> g_core;
    std::vector<std::wstring> g_mods;
    constexpr UINT kStartMessage = WM_APP + 0x472;

    void Log(const std::wstring& message) {
        OutputDebugStringW((message + L"\n").c_str());
        if (!g_logPath[0]) return;
        HANDLE f = CreateFileW(g_logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (f == INVALID_HANDLE_VALUE) return;
        SYSTEMTIME now{}; GetLocalTime(&now);
        wchar_t prefix[80] = {};
        swprintf_s(prefix, L"[%02u:%02u:%02u.%03u] ", now.wHour, now.wMinute,
            now.wSecond, now.wMilliseconds);
        std::wstring line = std::wstring(prefix) + message + L"\r\n";
        const int size = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (size > 1) {
            std::vector<char> utf8(size);
            if (WideCharToMultiByte(CP_UTF8, 0, line.c_str(), -1, utf8.data(), size, nullptr, nullptr)) {
                DWORD written = 0;
                WriteFile(f, utf8.data(), static_cast<DWORD>(size - 1), &written, nullptr);
            }
        }
        CloseHandle(f);
    }
    void LogError(const wchar_t* subject, DWORD error) {
        Log(std::wstring(subject) + L"; Win32 error=" + std::to_wstring(error));
    }
    bool GameDirectory() {
        DWORD n = GetModuleFileNameW(g_self, g_baseDir, MAX_PATH);
        if (!n || n >= MAX_PATH) return false;
        wchar_t* sep = wcsrchr(g_baseDir, L'\\');
        if (!sep) return false;
        sep[1] = 0;
        return true;
    }
    bool IsHospital() {
        wchar_t path[MAX_PATH] = {};
        DWORD n = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (!n || n >= MAX_PATH) return false;
        const wchar_t* filename = wcsrchr(path, L'\\');
        return _wcsicmp(filename ? filename + 1 : path, L"TPH.exe") == 0;
    }
    BOOL CALLBACK SetupForwarding(PINIT_ONCE, PVOID, PVOID*) {
        wchar_t path[MAX_PATH] = {};
        UINT n = GetSystemDirectoryW(path, MAX_PATH);
        if (!n || n + 14 >= MAX_PATH) return TRUE;
        wcscat_s(path, L"\\winhttp.dll");
        g_realWinHttp = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!g_realWinHttp || g_realWinHttp == g_self) return TRUE;
        for (unsigned i = 0; i < kExportCount; ++i)
            g_exports[i] = GetProcAddress(g_realWinHttp, kWinHTTPExports[i]);
        return TRUE;
    }
    FARPROC Resolve(unsigned index) {
        const DWORD old = GetLastError();
        InitOnceExecuteOnce(&g_realOnce, SetupForwarding, nullptr, nullptr);
        FARPROC fn = index < kExportCount ? g_exports[index] : nullptr;
        if (!fn) {
            OutputDebugStringW(L"UnderPressure Loader: required system WinHTTP export missing.\n");
            TerminateProcess(GetCurrentProcess(), ERROR_PROC_NOT_FOUND);
            return nullptr;
        }
        SetLastError(old);
        return fn;
    }
    bool ValidDllName(const std::wstring& filename) {
        if (filename.length() < 5 || filename.length() > 240) return false;
        if (_wcsicmp(filename.c_str() + filename.length() - 4, L".dll") != 0) return false;
        if (_wcsicmp(filename.c_str(), L"winhttp.dll") == 0) return false;
        for (wchar_t c : filename)
            if (c < 32 || wcschr(L"/\\:*?\"<>|", c)) return false;
        return true;
    }
    bool ContainsMod(const std::vector<std::wstring>& list, const std::wstring& name) {
        for (const auto& existing : list)
            if (_wcsicmp(existing.c_str(), name.c_str()) == 0) return true;
        return false;
    }
    // Core is never configured in loader.ini. Every DLL in Mods\Core is
    // preloaded into Mono (or into the native process) before any mod is started.
    void ReadCore() {
        g_core.clear();
        WIN32_FIND_DATAW file{};
        const std::wstring pattern = std::wstring(g_coreDir) + L"*.dll";
        HANDLE search = FindFirstFileW(pattern.c_str(), &file);
        if (search != INVALID_HANDLE_VALUE) {
            do {
                const std::wstring name(file.cFileName);
                if ((file.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ||
                    !ValidDllName(name) || ContainsMod(g_core, name)) continue;
                g_core.push_back(name);
            } while (FindNextFileW(search, &file));
            FindClose(search);
        }
        std::sort(g_core.begin(), g_core.end(), [](const std::wstring& a, const std::wstring& b) {
            return _wcsicmp(a.c_str(), b.c_str()) < 0;
        });
        Log(L"CORE DLLs discovered: " + std::to_wstring(g_core.size()));
        for (size_t i = 0; i < g_core.size(); ++i)
            Log(L"CORE " + std::to_wstring(i + 1) + L": " + g_core[i]);
        if (g_core.empty()) Log(L"WARNING: Mods\\Core contains no DLLs");
    }

    void ReadMods(const std::wstring& ini) {
        g_mods.clear();
        // Preserve the Museum INI format: [Mods] 1=File.dll, 2=Another.dll...
        for (unsigned i = 1; i <= 128; ++i) {
            wchar_t key[20] = {};
            swprintf_s(key, L"%u", i);
            wchar_t value[512] = {};
            DWORD length = GetPrivateProfileStringW(L"Mods", key, L"", value, 512, ini.c_str());
            if (!length) continue;
            const std::wstring name(value);
            if (!ValidDllName(name)) { Log(L"REJECTED INI NAME: " + name); continue; }
            if (ContainsMod(g_core, name)) {
                Log(L"SKIPPED INI CORE ENTRY (loaded automatically): " + name);
                continue;
            }
            if (ContainsMod(g_mods, name)) { Log(L"SKIPPED INI DUPLICATE: " + name); continue; }
            g_mods.push_back(name);
        }
        const size_t named = g_mods.size();
        // Also discover any additional DLLs in Mods without changing loader.cpp.
        // Explicit INI entries always keep their order; discovered files follow alphabetically.
        WIN32_FIND_DATAW file{};
        std::wstring pattern = std::wstring(g_modsDir) + L"*.dll";
        HANDLE search = FindFirstFileW(pattern.c_str(), &file);
        std::vector<std::wstring> discovered;
        if (search != INVALID_HANDLE_VALUE) {
            do {
                const std::wstring name(file.cFileName);
                if ((file.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || !ValidDllName(name) ||
                    ContainsMod(g_mods, name) || ContainsMod(discovered, name)) continue;
                if (ContainsMod(g_core, name)) {
                    Log(L"SKIPPED ROOT DLL (also present in Core): " + name);
                    continue;
                }
                discovered.push_back(name);
            } while (FindNextFileW(search, &file));
            FindClose(search);
        }
        std::sort(discovered.begin(), discovered.end(), [](const std::wstring& a, const std::wstring& b) {
            return _wcsicmp(a.c_str(), b.c_str()) < 0;
        });
        g_mods.insert(g_mods.end(), discovered.begin(), discovered.end());
        Log(L"INI mod entries: " + std::to_wstring(named) +
            L"; additionally discovered DLLs: " + std::to_wstring(discovered.size()));
        for (unsigned i=0; i<g_mods.size(); ++i)
            Log(L"MOD " + std::to_wstring(i+1) + L": " + g_mods[i]);
    }

    using MonoGetRootDomain = void* (__cdecl*)();
    using MonoThreadAttach = void* (__cdecl*)(void*);
    using MonoDomainAssemblyOpen = void* (__cdecl*)(void*, const char*);
    using MonoAssemblyGetImage = void* (__cdecl*)(void*);
    using MonoClassFromName = void* (__cdecl*)(void*, const char*, const char*);
    using MonoClassGetMethodFromName = void* (__cdecl*)(void*, const char*, int);
    using MonoRuntimeInvoke = void* (__cdecl*)(void*, void*, void**, void**);
    using MonoObjectNew = void* (__cdecl*)(void*, void*);
    using MonoStringNew = void* (__cdecl*)(void*, const char*);
    using MonoClassGetType = void* (__cdecl*)(void*);
    using MonoTypeGetObject = void* (__cdecl*)(void*, void*);
    using MonoImageGetTableInfo = const void* (__cdecl*)(void*, int);
    using MonoTableInfoGetRows = int (__cdecl*)(const void*);
    using MonoMetadataDecodeRow = void (__cdecl*)(const void*, int, uint32_t*, int);
    using MonoMetadataStringHeap = const char* (__cdecl*)(void*, uint32_t);
    using MonoClassIsSubclassOf = int (__cdecl*)(void*, void*, int);
    using MonoClassGetMethods = void* (__cdecl*)(void*, void**);
    using MonoMethodGetName = const char* (__cdecl*)(void*);
    using MonoMethodSignature = void* (__cdecl*)(void*);
    using MonoSignatureGetParamCount = uint32_t (__cdecl*)(void*);
    using MonoSignatureGetParams = void* (__cdecl*)(void*, void**);
    using MonoTypeGetName = char* (__cdecl*)(void*);
    using MonoFree = void (__cdecl*)(void*);

    struct MonoApi {
        void* domain{};
        MonoDomainAssemblyOpen assemblyOpen{};
        MonoAssemblyGetImage getImage{};
        MonoClassFromName classFromName{};
        MonoClassGetMethodFromName methodFromName{};
        MonoRuntimeInvoke invoke{};
        MonoObjectNew objectNew{};
        MonoStringNew stringNew{};
        MonoClassGetType classGetType{};
        MonoTypeGetObject typeGetObject{};
        MonoImageGetTableInfo imageTable{};
        MonoTableInfoGetRows tableRows{};
        MonoMetadataDecodeRow decodeRow{};
        MonoMetadataStringHeap stringHeap{};
        MonoClassIsSubclassOf subclass{};
        MonoClassGetMethods classMethods{};
        MonoMethodGetName methodName{};
        MonoMethodSignature methodSignature{};
        MonoSignatureGetParamCount paramCount{};
        MonoSignatureGetParams signatureParams{};
        MonoTypeGetName typeName{};
        MonoFree monoFree{};
    };
    bool LoadMonoApi(MonoApi& api) {
        HMODULE mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
        if (!mono) mono = GetModuleHandleW(L"mono-2.0-sgen.dll");
        if (!mono) mono = GetModuleHandleW(L"mono.dll");
        if (!mono) return false;
        auto get = [mono](const char* name) { return GetProcAddress(mono, name); };
        auto root = reinterpret_cast<MonoGetRootDomain>(get("mono_get_root_domain"));
        auto attach = reinterpret_cast<MonoThreadAttach>(get("mono_thread_attach"));
        api.assemblyOpen = reinterpret_cast<MonoDomainAssemblyOpen>(get("mono_domain_assembly_open"));
        api.getImage = reinterpret_cast<MonoAssemblyGetImage>(get("mono_assembly_get_image"));
        api.classFromName = reinterpret_cast<MonoClassFromName>(get("mono_class_from_name"));
        api.methodFromName = reinterpret_cast<MonoClassGetMethodFromName>(get("mono_class_get_method_from_name"));
        api.invoke = reinterpret_cast<MonoRuntimeInvoke>(get("mono_runtime_invoke"));
        api.objectNew = reinterpret_cast<MonoObjectNew>(get("mono_object_new"));
        api.stringNew = reinterpret_cast<MonoStringNew>(get("mono_string_new"));
        api.classGetType = reinterpret_cast<MonoClassGetType>(get("mono_class_get_type"));
        api.typeGetObject = reinterpret_cast<MonoTypeGetObject>(get("mono_type_get_object"));
        api.imageTable = reinterpret_cast<MonoImageGetTableInfo>(get("mono_image_get_table_info"));
        api.tableRows = reinterpret_cast<MonoTableInfoGetRows>(get("mono_table_info_get_rows"));
        api.decodeRow = reinterpret_cast<MonoMetadataDecodeRow>(get("mono_metadata_decode_row"));
        api.stringHeap = reinterpret_cast<MonoMetadataStringHeap>(get("mono_metadata_string_heap"));
        api.subclass = reinterpret_cast<MonoClassIsSubclassOf>(get("mono_class_is_subclass_of"));
        api.classMethods = reinterpret_cast<MonoClassGetMethods>(get("mono_class_get_methods"));
        api.methodName = reinterpret_cast<MonoMethodGetName>(get("mono_method_get_name"));
        api.methodSignature = reinterpret_cast<MonoMethodSignature>(get("mono_method_signature"));
        api.paramCount = reinterpret_cast<MonoSignatureGetParamCount>(get("mono_signature_get_param_count"));
        api.signatureParams = reinterpret_cast<MonoSignatureGetParams>(get("mono_signature_get_params"));
        api.typeName = reinterpret_cast<MonoTypeGetName>(get("mono_type_get_name"));
        api.monoFree = reinterpret_cast<MonoFree>(get("mono_free"));
        if (!root || !attach || !api.assemblyOpen || !api.getImage || !api.classFromName ||
            !api.methodFromName || !api.invoke || !api.objectNew || !api.stringNew ||
            !api.classGetType || !api.typeGetObject || !api.imageTable || !api.tableRows ||
            !api.decodeRow || !api.stringHeap || !api.subclass || !api.classMethods ||
            !api.methodName || !api.methodSignature || !api.paramCount ||
            !api.signatureParams || !api.typeName) {
            Log(L"MONO ERROR: required embedding exports unavailable");
            return false;
        }
        api.domain = root();
        return api.domain != nullptr && attach(api.domain) != nullptr;
    }
    bool ToUtf8(const std::wstring& path, std::vector<char>& utf8) {
        const int n = WideCharToMultiByte(CP_UTF8,0,path.c_str(),-1,nullptr,0,nullptr,nullptr);
        if (n<=0) return false;
        utf8.resize(n);
        return WideCharToMultiByte(CP_UTF8,0,path.c_str(),-1,utf8.data(),n,nullptr,nullptr) > 0;
    }
    void* OpenAssembly(MonoApi& api, const std::wstring& path) {
        std::vector<char> utf8;
        return ToUtf8(path, utf8) ? api.assemblyOpen(api.domain, utf8.data()) : nullptr;
    }
    // Overloaded Unity method: select AddComponent(System.Type), never the generic overload.
    void* FindAddComponent(MonoApi& api, void* gameObjectClass) {
        void* cursor = nullptr;
        while (void* method = api.classMethods(gameObjectClass, &cursor)) {
            if (strcmp(api.methodName(method), "AddComponent") != 0) continue;
            void* sig = api.methodSignature(method);
            if (!sig || api.paramCount(sig) != 1) continue;
            void* iter = nullptr;
            void* param = api.signatureParams(sig, &iter);
            char* type = param ? api.typeName(param) : nullptr;
            const bool match = type && strcmp(type, "System.Type") == 0;
            if (type && api.monoFree) api.monoFree(type);
            if (match) return method;
        }
        return nullptr;
    }
    bool CreateManagedHost(MonoApi& api, void*& host, void*& addComponent) {
        const std::wstring corePath = std::wstring(g_baseDir) + L"TPH_Data\\Managed\\UnityEngine.CoreModule.dll";
        void* assembly = OpenAssembly(api, corePath);
        void* image = assembly ? api.getImage(assembly) : nullptr;
        void* unityObject = image ? api.classFromName(image,"UnityEngine","Object") : nullptr;
        void* gameObject = image ? api.classFromName(image,"UnityEngine","GameObject") : nullptr;
        if (!unityObject || !gameObject) { Log(L"ERROR: UnityEngine.CoreModule not available"); return false; }
        void* ctor = api.methodFromName(gameObject,".ctor",1);
        void* preserve = api.methodFromName(unityObject,"DontDestroyOnLoad",1);
        addComponent = FindAddComponent(api,gameObject);
        if (!ctor || !preserve || !addComponent) { Log(L"ERROR: Unity GameObject API mismatch"); return false; }
        host = api.objectNew(api.domain,gameObject);
        if (!host) return false;
        void* exception = nullptr;
        void* name = api.stringNew(api.domain,"UnderPressure Loader");
        void* ctorArgs[] = {name};
        api.invoke(ctor,host,ctorArgs,&exception);
        if (exception) { Log(L"ERROR: GameObject creation failed"); return false; }
        void* persistArgs[] = {host};
        exception = nullptr;
        api.invoke(preserve,nullptr,persistArgs,&exception);
        if (exception) { Log(L"ERROR: DontDestroyOnLoad failed"); return false; }
        return true;
    }
    bool HasSuffix(const char* s, const char* suffix) {
        size_t n = strlen(s), m = strlen(suffix);
        return n >= m && strcmp(s + n - m, suffix) == 0;
    }
    // The managed entrypoint is convention based, not a DLL filename or hardcoded class.
    // Only MonoBehaviour classes ending in "Plugin" and declaring Awake() are started.
    int StartManagedAssembly(MonoApi& api, void* image, void* monoBehaviour, void*& host, void*& addComponent,
                             const std::wstring& modName) {
        const void* table = api.imageTable(image, 2); // MONO_TABLE_TYPEDEF
        if (!table) return 0;
        const int rows = api.tableRows(table);
        int started = 0;
        for (int row = 1; row < rows; ++row) {
            uint32_t columns[6] = {};
            api.decodeRow(table,row,columns,6);
            const char* name = api.stringHeap(image,columns[1]);
            const char* ns = api.stringHeap(image,columns[2]);
            if (!name || !HasSuffix(name,"Plugin")) continue;
            void* klass = api.classFromName(image,ns ? ns : "",name);
            if (!klass || !api.subclass(klass,monoBehaviour,0)) continue;
            if (!api.methodFromName(klass,"Awake",0)) continue;
            if (!host && !CreateManagedHost(api,host,addComponent)) return -1;
            void* type = api.classGetType(klass);
            void* managedType = type ? api.typeGetObject(api.domain,type) : nullptr;
            if (!managedType) { Log(L"ERROR: cannot get Mono Type for " + modName); return -1; }
            void* args[] = {managedType};
            void* exception = nullptr;
            void* component = api.invoke(addComponent,host,args,&exception);
            if (!component || exception) {
                Log(L"ERROR: AddComponent failed for " + modName + L". Check dependencies and C# errors.");
                return -1;
            }
            ++started;
            Log(L"STARTED managed component from " + modName);
        }
        return started;
    }
    // All Core DLLs load first; they are dependencies and never MonoBehaviour plugins.
    // Returns the number of failed Core DLLs, leaving plugin initialization untouched.
    int LoadCoreAssemblies(MonoApi& api) {
        int managed = 0, native = 0, failures = 0;
        Log(L"CORE LOAD BEGIN");
        for (const auto& filename : g_core) {
            const std::wstring full = std::wstring(g_coreDir) + filename;
            Log(L"CORE LOAD: " + filename);
            void* assembly = OpenAssembly(api, full);
            if (assembly && api.getImage(assembly)) {
                ++managed;
                Log(L"CORE LOADED managed: " + filename);
                continue;
            }
            // Allow native support libraries in Core too. Core DLLs are not mods.
            HMODULE module = LoadLibraryExW(full.c_str(), nullptr,
                LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            if (module) { ++native; Log(L"CORE LOADED native: " + filename); }
            else { ++failures; LogError((L"CORE FAILED: " + filename).c_str(), GetLastError()); }
        }
        Log(L"CORE PASS COMPLETE: managed=" + std::to_wstring(managed) +
            L"; native=" + std::to_wstring(native) +
            L"; failures=" + std::to_wstring(failures));
        return failures;
    }
    // Return 0 when Unity/Mono are not ready, 1 when the load pass completed, -1 on failure.
    int LoadConfiguredMods() {
        MonoApi api{};
        if (!LoadMonoApi(api)) return 0;
        if (LoadCoreAssemblies(api) != 0) {
            Log(L"MOD LOAD ABORTED: Core dependencies failed to load");
            return -1;
        }
        ReadMods(std::wstring(g_modsDir) + L"loader.ini");
        if (g_mods.empty()) {
            Log(L"No plugin DLLs in Mods; Core initialization complete");
            return 1;
        }
        void* host = nullptr;
        void* addComponent = nullptr;
        const std::wstring corePath = std::wstring(g_baseDir) + L"TPH_Data\\Managed\\UnityEngine.CoreModule.dll";
        void* coreAssembly = OpenAssembly(api,corePath);
        void* coreImage = coreAssembly ? api.getImage(coreAssembly) : nullptr;
        void* mbClass = coreImage ? api.classFromName(coreImage,"UnityEngine","MonoBehaviour") : nullptr;
        if (!mbClass) return 0;
        int failures = 0, managed=0, native=0, plugins=0;
        for (const auto& filename : g_mods) {
            const std::wstring full = std::wstring(g_modsDir) + filename;
            if (GetFileAttributesW(full.c_str()) == INVALID_FILE_ATTRIBUTES) {
                Log(L"MISSING: " + filename); ++failures; continue;
            }
            Log(L"LOAD BEGIN: " + filename);
            void* assembly = OpenAssembly(api,full);
            if (assembly) {
                void* image = api.getImage(assembly);
                if (!image) {Log(L"ERROR: managed image missing for " + filename); ++failures; continue;}
                ++managed;
                const int count = StartManagedAssembly(api,image,mbClass,host,addComponent,filename);
                if (count < 0) ++failures;
                else { plugins += count; Log(L"LOADED managed: " + filename + L"; plugin components=" + std::to_wstring(count)); }
            } else {
                HMODULE module = LoadLibraryExW(full.c_str(),nullptr,
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                if (module) { ++native; Log(L"LOADED native: " + filename); }
                else { ++failures; LogError((L"FAILED: " + filename).c_str(),GetLastError()); }
            }
        }
        Log(L"LOAD PASS COMPLETE: managed=" + std::to_wstring(managed) +
            L"; native=" + std::to_wstring(native) + L"; components=" + std::to_wstring(plugins) +
            L"; failures=" + std::to_wstring(failures));
        return failures ? -1 : 1;
    }
    LRESULT CALLBACK MainThreadHook(int code, WPARAM wp, LPARAM lp) {
        if (code >= 0 && lp) {
            MSG* message = reinterpret_cast<MSG*>(lp);
            if (message->message == kStartMessage && message->hwnd == g_gameWindow) {
                message->message = WM_NULL;
                if (InterlockedCompareExchange(&g_startState, 1, 0) == 0) {
                    const int result = LoadConfiguredMods();
                    InterlockedExchange(&g_startState, result > 0 ? 2 : result < 0 ? 3 : 0);
                }
            }
        }
        return CallNextHookEx(g_hook,code,wp,lp);
    }
    BOOL CALLBACK FindUnityWindow(HWND window, LPARAM value) {
        DWORD processId = 0; GetWindowThreadProcessId(window,&processId);
        if (processId != GetCurrentProcessId()) return TRUE;
        wchar_t name[128] = {};
        if (GetClassNameW(window,name,128) && wcscmp(name,L"UnityWndClass") == 0) {
            *reinterpret_cast<HWND*>(value) = window;
            return FALSE;
        }
        return TRUE;
    }
    DWORD WINAPI Worker(LPVOID) {
        HMODULE pinned = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                                reinterpret_cast<LPCWSTR>(&Worker),&pinned)) return 1;
        if (!GameDirectory()) return 2;
        const std::wstring mods = std::wstring(g_baseDir) + L"Mods";
        CreateDirectoryW(mods.c_str(),nullptr);
        wcscpy_s(g_modsDir, (mods + L"\\").c_str());
        const std::wstring core = mods + L"\\Core";
        wcscpy_s(g_coreDir, (core + L"\\").c_str());
        // Shared log: native loader and the managed modules append to the same file.
        wcscpy_s(g_logPath, (mods + L"\\mods.log").c_str());
        Log(L"UnderPressure Loader - native WinHTTP proxy initialized");
        if (!IsHospital()) {Log(L"SKIP: host is not TPH.exe"); return 0;}
        const std::wstring ini = mods + L"\\loader.ini";
        if (!GetPrivateProfileIntW(L"Loader",L"Enabled",1,ini.c_str())) {
            Log(L"Disabled by Mods\\loader.ini"); return 0;
        }
        InitOnceExecuteOnce(&g_realOnce,SetupForwarding,nullptr,nullptr);
        if (!g_realWinHttp) {Log(L"ERROR: Windows WinHTTP forwarding unavailable"); return 3;}
        Log(L"Windows WinHTTP forwarding ready");
        if (!CreateDirectoryW(core.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
            LogError(L"WARNING: cannot create Mods\\Core", GetLastError());
        // Native dependencies may be resolved from Core when loading any mod.
        if (!AddDllDirectory(core.c_str()))
            LogError(L"WARNING: cannot register Core as a native DLL directory", GetLastError());
        ReadCore();

        Log(L"Waiting for UnityWndClass and Mono runtime");
        for (int i = 0; i < 1200; ++i) {
            HWND found = nullptr;
            EnumWindows(FindUnityWindow,reinterpret_cast<LPARAM>(&found));
            if (found) { g_gameWindow = found; break; }
            Sleep(100);
        }
        if (!g_gameWindow) {Log(L"TIMEOUT: Unity window not found"); return 4;}
        const DWORD thread = GetWindowThreadProcessId(g_gameWindow,nullptr);
        g_hook = SetWindowsHookExW(WH_GETMESSAGE,MainThreadHook,g_self,thread);
        if (!g_hook) {LogError(L"ERROR: main-thread hook",GetLastError()); return 5;}
        Log(L"Unity thread hook ready; waiting to load Mods DLLs");
        for (int i=0; i<120; ++i) {
            LONG state = InterlockedCompareExchange(&g_startState,0,0);
            if (state==2 || state==3) break;
            PostMessageW(g_gameWindow,kStartMessage,0,0);
            Sleep(500);
        }
        if (InterlockedCompareExchange(&g_startState,0,0)==0)
            Log(L"TIMEOUT: Mono initialization did not complete");
        if (g_hook) {UnhookWindowsHookEx(g_hook);g_hook=nullptr;}
        return 0;
    }
}
extern "C" FARPROC __cdecl resolve_export(unsigned index) { return Resolve(index); }
extern "C" BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason==DLL_PROCESS_ATTACH) {
        g_self=module;
        DisableThreadLibraryCalls(module);
        HANDLE thread=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
