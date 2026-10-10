using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Xml.Linq;
using Microsoft.Win32;

[assembly: AssemblyTitle("PatchFontBridge")]
[assembly: AssemblyDescription("Windows localization DLL proxy generator")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

namespace WinmmProxyMaker {
sealed class Section { public uint Va, Size, Raw, RawSize, Flags; }
sealed class Export { public int Slot; public ushort Ordinal; public string Name; }
sealed class Pe {
    readonly byte[] b; readonly List<Section> sections = new List<Section>();
    public readonly ushort Machine; public readonly bool IsDll;
    uint exportRva, exportSize, importRva; public readonly string PathName;
    ushort U16(int p) { Check(p,2); return BitConverter.ToUInt16(b,p); }
    uint U32(int p) { Check(p,4); return BitConverter.ToUInt32(b,p); }
    ulong U64(int p) { Check(p,8); return BitConverter.ToUInt64(b,p); }
    void Check(int p,int n) { if(p<0 || n<0 || p>b.Length-n) throw new InvalidDataException("PE 檔案範圍錯誤："+PathName); }
    int At(uint r) { foreach(var s in sections) if(r>=s.Va && (ulong)r<s.Va+s.Size) { uint delta=r-s.Va; if(delta>=s.RawSize) break; long p=(long)s.Raw+delta; if(p>int.MaxValue) break; Check((int)p,1); return (int)p; } throw new InvalidDataException("PE RVA 無法讀取："+PathName); }
    string Z(uint r) { int p=At(r), end=p; while(end<b.Length && b[end]!=0 && end-p<32768) end++; if(end==b.Length || end-p>=32768) throw new InvalidDataException("PE 字串格式錯誤"); return Encoding.ASCII.GetString(b,p,end-p); }
    public Pe(string path) : this(File.ReadAllBytes(path),System.IO.Path.GetFullPath(path)) {}
    public Pe(byte[] data,string label) {
        PathName=label; b=data;
        if(U16(0)!=0x5a4d) throw new InvalidDataException("不是 Windows PE 檔案");
        int p=checked((int)U32(0x3c)); if(U32(p)!=0x4550) throw new InvalidDataException("PE 簽章錯誤");
        Machine=U16(p+4); if(Machine!=0x14c && Machine!=0x8664) throw new InvalidDataException("只支援 x86 / x64");
        IsDll=(U16(p+22)&0x2000)!=0; int count=U16(p+6), opt=p+24;
        ushort magic=U16(opt); if((Machine==0x14c && magic!=0x10b)||(Machine==0x8664 && magic!=0x20b)) throw new InvalidDataException("PE 架構不一致");
        int dd=opt+(magic==0x10b?96:112); exportRva=U32(dd); exportSize=U32(dd+4); importRva=U32(dd+8);
        int start=opt+U16(p+20); if(count>96) throw new InvalidDataException("PE 區段過多");
        for(int i=0;i<count;i++) { int q=start+i*40; sections.Add(new Section { Va=U32(q+12), Size=Math.Max(U32(q+8),U32(q+16)), RawSize=U32(q+16), Raw=U32(q+20), Flags=U32(q+36) }); }
    }
    public Dictionary<string,List<string>> Imports() {
        var result=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase); if(importRva==0) return result;
        int q=At(importRva);
        for(int d=0;d<2048;d++,q+=20) {
            uint oft=U32(q), name=U32(q+12), ft=U32(q+16); if(name==0 && oft==0 && ft==0) return result;
            string dll=Z(name); var names=new List<string>(); int t=At(oft!=0?oft:ft), width=Machine==0x14c?4:8;
            for(int i=0;i<65536;i++,t+=width) {
                ulong v=width==4?U32(t):U64(t); if(v==0) break;
                ulong mask=width==4?0x80000000UL:0x8000000000000000UL;
                names.Add((v&mask)!=0?"#"+(v&0xffff):Z(checked((uint)v)+2));
            }
            result[dll]=names;
        } throw new InvalidDataException("PE 匯入表過長");
    }
    public List<Export> Exports(out int slots) {
        if(exportRva==0) throw new InvalidDataException("DLL 沒有匯出表");
        int q=At(exportRva); uint basis=U32(q+16), count=U32(q+20), names=U32(q+24);
        if(count==0 || count>65535 || names>65535 || basis+count>65536) throw new InvalidDataException("匯出序號超出支援範圍");
        slots=(int)count; int funcs=At(U32(q+28)); var map=new Dictionary<int,string>();
        if(names>0) { int np=At(U32(q+32)), op=At(U32(q+36)); for(int i=0;i<names;i++) {
            int slot=U16(op+i*2); if(slot>=count || map.ContainsKey(slot)) throw new InvalidDataException("此 DLL 包含多名稱同序號或無效匯出，暫不支援");
            map[slot]=Z(U32(np+i*4));
        } }
        var exports=new List<Export>();
        for(int i=0;i<count;i++) { uint r=U32(funcs+i*4); if(r==0) continue;
            bool forwarded=r>=exportRva && (ulong)r<exportRva+exportSize;
            if(!forwarded && !sections.Any(s=>r>=s.Va && (ulong)r<s.Va+s.Size && (s.Flags&0x20000000)!=0)) throw new InvalidDataException("此 DLL 包含資料匯出，不能使用函式代理");
            string name; map.TryGetValue(i,out name);
            if(name!=null && (name.Any(c=>c<=32 || c=='=' || c=='"') || name.StartsWith(";"))) throw new InvalidDataException("不支援此匯出名稱格式");
            exports.Add(new Export { Slot=i, Ordinal=(ushort)(basis+i), Name=name });
        }
        return exports;
    }
}
sealed class Candidate { public string Name, SystemPath, Evidence; public override string ToString(){return Name+" — "+Evidence;} }
sealed class Analysis { public Pe Game, Patch; public string GameHash, PatchHash; public List<Candidate> Candidates=new List<Candidate>(); }
static class Core {
    static readonly string[] supported={"version.dll","dinput8.dll","dinput.dll","dsound.dll","ddraw.dll","d3d8.dll","d3d9.dll","d3d11.dll","dxgi.dll","xinput1_3.dll","xinput1_4.dll","wininet.dll","winhttp.dll"};
    public static string Hash(string path) { using(var sha=SHA256.Create()) using(var f=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
    public static string Arch(Pe p) { return p.Machine==0x14c?"x86":"x64"; }
    public static string FontHint(Analysis a) {
        string root=Path.GetDirectoryName(a.Game.PathName), ini=Path.Combine(root,"FontHook.ini"), log=Path.Combine(root,"FontHook.trace.log");
        var hints=new List<string>();
        if(File.Exists(ini) && File.ReadLines(ini).Any(s=>s.Trim()=="PickerShowOnStartup=1")) hints.Add("SimpleFontHook 設為啟動時顯示選擇器。若彈出多個視窗，可將 PickerShowOnStartup 設為 0，再使用快捷鍵開啟。");
        if(File.Exists(log)) { string line=File.ReadLines(log).LastOrDefault(s=>s.Contains("Registered toggle hotkey"));if(line!=null)hints.Add("上次紀錄的快捷鍵："+line.Substring(line.IndexOf("Registered toggle hotkey")+"Registered toggle hotkey".Length).Trim()); }
        string winmm=Path.Combine(root,"winmm.dll");
        if(File.Exists(winmm) && Hash(winmm)==a.PatchHash) hints.Add("根目錄的 winmm.dll 與選取的補丁是同一檔案；另外載入副本可能產生重複 hook／視窗。");
        return string.Join("\n",hints);
    }
    static string SystemPath(string name, ushort machine) {
        string windir=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string dir=machine==0x14c && Environment.Is64BitOperatingSystem?"SysWOW64":(!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem?"Sysnative":"System32");
        return Path.Combine(windir,dir,name);
    }
    static bool Known(string name) {
        if(name.StartsWith("api-ms-",StringComparison.OrdinalIgnoreCase)||name.StartsWith("ext-ms-",StringComparison.OrdinalIgnoreCase)) return true;
        using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs")) {
            if(k!=null) foreach(string n in k.GetValueNames()) if(string.Equals(Convert.ToString(k.GetValue(n)),name,StringComparison.OrdinalIgnoreCase)) return true;
        } return false;
    }
    public static Analysis Analyze(string game,string patch) {var a=AnalyzeGame(game);BindPatch(a,patch);return a;}
    public static void BindPatch(Analysis a,string patch){a.Patch=new Pe(patch);if(!a.Patch.IsDll)throw new Exception("請選擇漢化 DLL");if(a.Game.Machine!=a.Patch.Machine)throw new Exception("遊戲與補丁位元數不同");a.PatchHash=Hash(patch);}
    public static Analysis AnalyzeGame(string game) {
        var a=new Analysis {Game=new Pe(game)};
        if(a.Game.IsDll)throw new Exception("請選擇遊戲 EXE");
        a.GameHash=Hash(game);
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase); string root=Path.GetDirectoryName(a.Game.PathName);
        Action<Pe,string,int> scan=null;
        scan=(pe,chain,depth)=>{
            if(depth>8 || !visited.Add(pe.PathName)) return;
            foreach(var pair in pe.Imports()) {
                string n=pair.Key.ToLowerInvariant(); if(Path.GetFileName(n)!=n) continue;
                if(supported.Contains(n) && !Known(n)) {
                    string sys=SystemPath(n,a.Game.Machine);
                    if(File.Exists(sys) && !a.Candidates.Any(c=>c.Name==n)) {
                        var sp=new Pe(sys); if(sp.Machine!=a.Game.Machine) continue;
                        int slots; var es=sp.Exports(out slots);
                        if(Environment.GetEnvironmentVariable("PFB_PREPARE_PROXIES")!="1") {
                            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("proxy."+Arch(a.Game)+"."+n)) {
                                if(stream==null)continue;
                                using(var memory=new MemoryStream()) {
                                    stream.CopyTo(memory);int ps;var provided=new Pe(memory.ToArray(),n).Exports(out ps);
                                    if(provided.Count!=es.Count||es.Any(e=>!provided.Any(p=>p.Name==e.Name&&p.Ordinal==e.Ordinal)))continue;
                                }
                            }
                        }
                        foreach(var api in pair.Value) if(!es.Any(e=>api.StartsWith("#")?"#"+e.Ordinal==api:e.Name==api)) throw new InvalidDataException(n+" 缺少遊戲要求的 API："+api);
                        a.Candidates.Add(new Candidate{Name=n,SystemPath=sys,Evidence=chain});
                    }
                }
                string local=Path.Combine(root,pair.Key);
                if(File.Exists(local) && n!="winmm.dll" && !supported.Contains(n)) {
                    var lp=new Pe(local); if(lp.Machine==a.Game.Machine) scan(lp,chain+" → "+pair.Key,depth+1);
                }
            }
        };
        scan(a.Game,Path.GetFileName(game),0);
        a.Candidates=a.Candidates.OrderBy(c=>c.Name=="version.dll"?0:1).ThenBy(c=>c.Name).ToList();
        return a;
    }
    static string Toolchain() {
        string vswhere=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Microsoft Visual Studio\Installer\vswhere.exe");
        if(!File.Exists(vswhere)) throw new Exception("找不到 Visual Studio Build Tools；請安裝 C++ 桌面開發工作負載");
        var psi=new ProcessStartInfo(vswhere,"-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath") {UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true};
        using(var p=Process.Start(psi)) { string location=p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); string bat=Path.Combine(location,@"Common7\Tools\VsDevCmd.bat"); if(!File.Exists(bat)) throw new Exception("找不到 MSVC x86/x64 編譯環境"); return bat; }
    }
    static string Template() { using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("proxy.cpp")) using(var r=new StreamReader(s)) return r.ReadToEnd(); }
    static void PreparedProxy(string arch,string host,string dest,string payload,string bridge,bool early,List<Export> expected,ushort machine) {
        byte[] bytes;
        using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("proxy."+arch+"."+host)) {
            if(s==null)throw new Exception("此版本未內建 "+arch+" "+host+" 代理；請選擇其他可用入口或取得新版工具。");
            using(var m=new MemoryStream()){s.CopyTo(m);bytes=m.ToArray();}
        }
        byte[] tag=Encoding.ASCII.GetBytes("PFB_CONFIGURATION_V1");int offset=-1;
        for(int i=0;i<=bytes.Length-2340;i++) {
            bool match=true;for(int j=0;j<tag.Length;j++)if(bytes[i+j]!=tag[j]){match=false;break;}
            if(match){if(offset>=0)throw new Exception("內建代理設定區重複");offset=i;}
        }
        if(offset<0)throw new Exception("內建代理缺少設定區");
        Action<int,int,string> put=(start,size,value)=>{byte[] valueBytes=Encoding.Unicode.GetBytes(value+"\0");if(valueBytes.Length>size)throw new Exception("代理路徑過長");Array.Clear(bytes,start,size);Array.Copy(valueBytes,0,bytes,start,valueBytes.Length);};
        put(offset+32,256,payload);put(offset+288,2048,bridge);
        Array.Copy(BitConverter.GetBytes(early?1u:0u),0,bytes,offset+2336,4);
        File.WriteAllBytes(dest,bytes);
        int slots;var pe=new Pe(dest);var actual=pe.Exports(out slots);
        if(pe.Machine!=machine||actual.Count!=expected.Count||expected.Any(e=>!actual.Any(x=>x.Ordinal==e.Ordinal&&x.Name==e.Name))) {
            File.Delete(dest);throw new Exception("內建 "+host+" 代理與此 Windows 的 API 匯出不符。請選擇其他入口或取得更新的代理包。");
        }
    }
    static Dictionary<string,string> FontResources(string patch,string gameRoot){
        byte[] bytes=File.ReadAllBytes(patch);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string decoded in new[]{Encoding.ASCII.GetString(bytes),Encoding.Unicode.GetString(bytes),Encoding.Unicode.GetString(bytes,Math.Min(1,bytes.Length),Math.Max(0,bytes.Length-1))})
            foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(decoded,@"(?i)(?<=\x00)[a-z0-9][a-z0-9_. -]{0,90}\.(?:ttf|otf|ttc)(?=\x00)"))names.Add(m.Value);
        var resources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string name in names){string[] options={Path.Combine(Path.GetDirectoryName(patch),name),Path.Combine(gameRoot,name)};string source=options.FirstOrDefault(File.Exists);if(source==null)throw new Exception("補丁引用的字型資源不存在："+name+"；請先將原補丁需要的字型放回遊戲目錄");resources[name]=source;}
        return resources;
    }
    static void Compile(string temp,string dest,string fileName,Action<string> log) {
        var psi=new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC"),"/d /c build.cmd") {WorkingDirectory=temp,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.Default,StandardErrorEncoding=Encoding.Default};
        string stdout,stderr;int code;
        using(var p=Process.Start(psi)){var err=p.StandardError.ReadToEndAsync();stdout=p.StandardOutput.ReadToEnd();p.WaitForExit();stderr=err.Result;code=p.ExitCode;}
        File.AppendAllText(Path.Combine(dest,"build.log"),stdout+stderr);
        if(code!=0)throw new Exception("編譯失敗，請查看 "+Path.Combine(dest,"build.log")+"\n"+stdout+stderr);
    }
    public static string Build(Analysis a,Candidate c,string output,bool early,Action<string> log,string placement="root",string originalMode="bak",FontPreset preset=null) {
        if(placement!="root"&&placement!="dll"&&placement!="patch"&&placement!="translation")throw new Exception("放置位置必須是 root、dll、patch 或 translation");
        if(originalMode!="bak"&&originalMode!="delete")throw new Exception("原檔處理必須是 bak 或 delete");
        if(Hash(a.Game.PathName)!=a.GameHash || Hash(a.Patch.PathName)!=a.PatchHash) throw new Exception("輸入檔案已變更，請重新分析");
        output=Path.GetFullPath(output);
        string root=Path.GetDirectoryName(a.Game.PathName);
        if(string.Equals(output.TrimEnd('\\'),root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)) throw new Exception("請選擇遊戲目錄以外的輸出資料夾，建立完成後再按安裝");
        string dest=Path.Combine(output,"proxy-"+Path.GetFileNameWithoutExtension(c.Name)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
        Directory.CreateDirectory(dest); string temp=Path.Combine(Path.GetTempPath(),"wpm-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try {
            var system=new Pe(c.SystemPath); int slots; var exports=system.Exports(out slots);
            string payload="translation_patch_"+a.PatchHash.Substring(0,8)+".dll";
            string folder=placement=="dll"?"dll":placement=="translation"?"漢化補丁":"";
            if(placement=="patch") {
                string dir=Path.GetDirectoryName(a.Patch.PathName),prefix=root.TrimEnd('\\')+"\\";
                if(!string.Equals(dir,root,StringComparison.OrdinalIgnoreCase)) {if(!dir.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new Exception("原漢化 DLL 不在遊戲目錄內，請選擇遊戲根目錄");folder=dir.Substring(prefix.Length);}
            }
            byte[] font=Integration.FontBinary(Arch(a.Game));
            string fontPath=Path.Combine(dest,"winmm.dll");File.WriteAllBytes(fontPath,font);
            if(new Pe(fontPath).Machine!=a.Game.Machine)throw new Exception("內建 SimpleFontHook 位元數錯誤");
            if(Hash(fontPath)==a.PatchHash||a.PatchHash=="2007a498181e1c6f7d36600e5dbb5dcedfa01b9583b129b9136e56924d219684"||a.PatchHash=="9e46b7532d8d029755cf702928b562f14d5afa403e1286b8ad49561cee5f8191"||a.PatchHash=="454cfcb97a6da41a23fd566e0e3453cb90ce5ce01d293c3a316a186223fa17b7")throw new Exception("你選取的是 SimpleFontHook 本身，請改選漢化 DLL");
            string fullFolder=folder.Length>0?Path.Combine(dest,folder):dest;Directory.CreateDirectory(fullFolder);
            if(Environment.GetEnvironmentVariable("PFB_PREPARE_PROXIES")!="1") {
                PreparedProxy(Arch(a.Game),c.Name,Path.Combine(fullFolder,c.Name),payload,"",early,exports,a.Game.Machine);
                File.Copy(a.Patch.PathName,Path.Combine(fullFolder,payload));
                if(folder.Length>0)PreparedProxy(Arch(a.Game),c.Name,Path.Combine(dest,c.Name),payload,Path.Combine(folder,c.Name),early,exports,a.Game.Machine);
                log("已準備內建代理："+Arch(a.Game)+" "+c.Name+"（不需安裝編譯工具）");
            } else {
            var ords=new int[slots]; var exportNames=Enumerable.Repeat("0",slots).ToArray(); foreach(var e in exports) {ords[e.Slot]=e.Ordinal;if(e.Name!=null)exportNames[e.Slot]="\""+e.Name.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";}
            string source=Template().Replace("@COUNT@",slots.ToString()).Replace("@ORDINALS@",string.Join(", ",ords)).Replace("@NAMES@",string.Join(", ",exportNames)).Replace("@HOST@",c.Name).Replace("@PAYLOAD@",payload)
                .Replace("@STARTUP@",early?"EnsureReady();":"HANDLE worker = CreateThread(0, 0, Worker, 0, 0, 0); if (worker) CloseHandle(worker);");
            string realLoad="UINT n = GetSystemDirectoryW(path, 32768); if (!n || n >= 32768 || wcscat_s(path, 32768, L\"\\\\\") || wcscat_s(path, 32768, hostName)) RaiseFailFastException(0,0,0); HMODULE real = LoadLibraryExW(path,0,LOAD_LIBRARY_SEARCH_SYSTEM32);";
            string moduleLoad="DWORD fontError=ERROR_FILENAME_EXCED_RANGE; if(GameFile(path,L\"winmm.dll\")){HMODULE font=LoadLibraryExW(path,0,LOAD_WITH_ALTERED_SEARCH_PATH);fontError=font?0:GetLastError();} DWORD error=ERROR_FILENAME_EXCED_RANGE; if(Sibling(path,payloadName)){HMODULE patch=LoadLibraryExW(path,0,LOAD_WITH_ALTERED_SEARCH_PATH);error=patch?0:GetLastError();} Log(error,fontError);";
            string fullSource=source.Replace("@REALLOAD@",realLoad).Replace("@MODULELOAD@",moduleLoad);
            File.WriteAllText(Path.Combine(temp,"proxy.cpp"),fullSource,Encoding.UTF8);
            var def=new StringBuilder("LIBRARY "+Path.GetFileNameWithoutExtension(c.Name)+"\r\nEXPORTS\r\n");
            foreach(var e in exports) def.AppendLine("  "+(e.Name??("Ordinal_"+e.Ordinal))+"=Stub_"+e.Slot+" @"+e.Ordinal+(e.Name==null?" NONAME":""));
            File.WriteAllText(Path.Combine(temp,"proxy.def"),def.ToString(),Encoding.ASCII);
            bool x86=a.Game.Machine==0x14c; string table=x86?"_gTargets":"gTargets", ensure=x86?"_EnsureReady":"EnsureReady";
            var asm=new StringBuilder(x86?".686\r\n.model flat\r\n":"");
            asm.AppendLine("EXTERN "+table+":"+(x86?"DWORD":"QWORD")); asm.AppendLine("EXTERN "+ensure+":PROC"); asm.AppendLine("EXTERN "+(x86?"_gReady":"gReady")+":DWORD"); asm.AppendLine(".code");
            foreach(var e in exports) {
                string stub="Stub_"+e.Slot, ready="ready_"+e.Slot; asm.AppendLine("PUBLIC "+stub); asm.AppendLine(stub+" PROC");
                if(x86) asm.AppendLine(" pushfd\r\n cmp DWORD PTR [_gReady], 1\r\n je "+ready+"\r\n pushad\r\n call "+ensure+"\r\n popad\r\n"+ready+":\r\n popfd\r\n jmp DWORD PTR ["+table+" + "+(e.Slot*4)+"]");
                else {
                    asm.AppendLine(" pushfq\r\n cmp DWORD PTR [gReady], 1\r\n je "+ready+"\r\n sub rsp, 0D0h");
                    string[] regs={"rax","rcx","rdx","r8","r9","r10","r11"};
                    for(int i=0;i<regs.Length;i++) asm.AppendLine(" mov QWORD PTR [rsp + "+(32+i*8)+"], "+regs[i]);
                    for(int i=0;i<6;i++) asm.AppendLine(" movdqu XMMWORD PTR [rsp + "+(96+i*16)+"], xmm"+i);
                    asm.AppendLine(" call "+ensure);
                    for(int i=0;i<6;i++) asm.AppendLine(" movdqu xmm"+i+", XMMWORD PTR [rsp + "+(96+i*16)+"]");
                    for(int i=0;i<regs.Length;i++) asm.AppendLine(" mov "+regs[i]+", QWORD PTR [rsp + "+(32+i*8)+"]");
                    asm.AppendLine(" add rsp, 0D0h\r\n"+ready+":\r\n popfq\r\n jmp QWORD PTR ["+table+" + "+(e.Slot*8)+"]");
                }
                asm.AppendLine(stub+" ENDP");
            }
            asm.AppendLine("END"); File.WriteAllText(Path.Combine(temp,"stubs.asm"),asm.ToString(),Encoding.ASCII);
            string command="@echo off\r\ncd /d \"%~dp0\"\r\ncall \""+Toolchain()+"\" -arch="+Arch(a.Game)+" -host_arch="+(Environment.Is64BitOperatingSystem?"x64":"x86")+" >nul\r\nif errorlevel 1 exit /b 1\r\n"+
                (x86?"ml /nologo /c /coff /Fostubs.obj stubs.asm":"ml64 /nologo /c /Fostubs.obj stubs.asm")+"\r\nif errorlevel 1 exit /b 1\r\ncl /nologo /LD /O2 /MT /EHsc /utf-8 proxy.cpp stubs.obj /link /DEF:proxy.def /OUT:"+c.Name+" kernel32.lib user32.lib\r\nexit /b %errorlevel%\r\n";
            File.WriteAllText(Path.Combine(temp,"build.cmd"),command,Encoding.Default);
            log("正在建置 "+Arch(a.Game)+" "+c.Name);
            Compile(temp,dest,c.Name,log);
            var generated=new Pe(Path.Combine(temp,c.Name)); int madeSlots; var made=generated.Exports(out madeSlots);
            if(generated.Machine!=a.Game.Machine || exports.Count!=made.Count || exports.Any(e=>!made.Any(m=>m.Ordinal==e.Ordinal && m.Name==e.Name))) throw new Exception("輸出代理的架構或匯出表驗證失敗");
            File.Copy(Path.Combine(temp,c.Name),Path.Combine(fullFolder,c.Name));File.Copy(a.Patch.PathName,Path.Combine(fullFolder,payload));
            string sourceFolder=Path.Combine(dest,"source");Directory.CreateDirectory(sourceFolder);
            foreach(string name in new[]{"proxy.cpp","proxy.def","stubs.asm","build.cmd"})File.Copy(Path.Combine(temp,name),Path.Combine(sourceFolder,name));
            if(folder.Length>0) {
                string bridge=source.Replace("@REALLOAD@","if(!Sibling(path,L\""+folder.Replace("\\","\\\\")+"\\\\"+c.Name+"\")) RaiseFailFastException(0,0,0); HMODULE real=LoadLibraryExW(path,0,LOAD_WITH_ALTERED_SEARCH_PATH);").Replace("@MODULELOAD@","Log(0,0);");
                File.WriteAllText(Path.Combine(temp,"proxy.cpp"),bridge,Encoding.UTF8);Compile(temp,dest,c.Name,log);
                var bp=new Pe(Path.Combine(temp,c.Name));int bs;var be=bp.Exports(out bs);if(bp.Machine!=a.Game.Machine||exports.Any(e=>!be.Any(b=>b.Name==e.Name&&b.Ordinal==e.Ordinal)))throw new Exception("入口橋接驗證失敗");
                File.Copy(Path.Combine(temp,c.Name),Path.Combine(dest,c.Name));File.WriteAllText(Path.Combine(sourceFolder,"bridge.cpp"),bridge,Encoding.UTF8);
                File.WriteAllText(Path.Combine(sourceFolder,"build-bridge.cmd"),command.Replace("proxy.cpp","bridge.cpp"),Encoding.Default);
            }
            }
            var manifest=new XElement("Package",new XAttribute("Format","2"),new XAttribute("Game",a.Game.PathName),new XAttribute("GameHash",a.GameHash),new XAttribute("Architecture",Arch(a.Game)),new XAttribute("Host",c.Name),new XAttribute("Payload",payload),new XAttribute("Startup",early?"early":"deferred"),new XAttribute("SystemHash",Hash(c.SystemPath)),new XAttribute("Exports",exports.Count),new XAttribute("Placement",placement),new XAttribute("Folder",folder),new XAttribute("Version","1.4"),new XAttribute("PrimaryProxy",folder.Length>0?Path.Combine(folder,c.Name):c.Name),new XAttribute("OriginalMode",originalMode),new XAttribute("SourcePatch",a.Patch.PathName),new XAttribute("SourcePatchHash",a.PatchHash));
            var files=new List<string>{"winmm.dll",folder.Length>0?Path.Combine(folder,c.Name):c.Name,folder.Length>0?Path.Combine(folder,payload):payload};if(folder.Length>0)files.Add(c.Name);
            foreach(var fontResource in FontResources(a.Patch.PathName,root)){
                string rel=folder.Length>0?Path.Combine(folder,fontResource.Key):fontResource.Key;
                File.Copy(fontResource.Value,Path.Combine(dest,rel),true);files.Add(rel);
                manifest.Add(new XElement("Resource",new XAttribute("Name",fontResource.Key),new XAttribute("Source",fontResource.Value),new XAttribute("Path",rel)));
                log("保留補丁字型資源："+rel);
            }
            foreach(string name in files)manifest.Add(new XElement("File",new XAttribute("Path",name),new XAttribute("Hash",Hash(Path.Combine(dest,name)))));
            if(preset!=null)manifest.Add(preset.Xml());
            new XDocument(manifest).Save(Path.Combine(dest,"package.xml"));
            log("完成："+dest+"\n匯出表已核對："+exports.Count+" 個，名稱與序號一致。"); return dest;
        } finally { Directory.Delete(temp,true); }
    }
    static void Stopped(string game) { foreach(var p in Process.GetProcesses()) using(p) { string path=null; try { path=p.MainModule.FileName; } catch(System.ComponentModel.Win32Exception) {} catch(NotSupportedException) {} catch(InvalidOperationException) {} if(string.Equals(path,game,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("請先關閉遊戲"); } }
    static string SafeChild(string root,string name) { if(name!=Path.GetFileName(name)||string.IsNullOrWhiteSpace(name)) throw new Exception("安裝清單包含無效檔名"); return Path.Combine(root,name); }
    static void AtomicCopy(string source,string target) { string tmp=target+".wpm-"+Guid.NewGuid().ToString("N"); File.Copy(source,tmp); try { if(File.Exists(target)) File.Replace(tmp,target,null); else File.Move(tmp,target); } finally { if(File.Exists(tmp)) File.Delete(tmp); } }
    public static void Install(string package,Action<string> log) {
        Integration.Install(package,log);
    }
    static void LegacyInstall(string package,Action<string> log) {
        var doc=XDocument.Load(Path.Combine(package,"package.xml")); var m=doc.Root; string game=(string)m.Attribute("Game"), root=Path.GetDirectoryName(game); Stopped(game);
        if(Hash(game)!=(string)m.Attribute("GameHash")) throw new Exception("遊戲 EXE 已變更，請重新建立代理");
        string marker=Path.Combine(root,"WinmmProxyMaker-install.xml"); if(File.Exists(marker)) throw new Exception("已有工具安裝紀錄；請先回復再安裝新版本");
        string backup=Path.Combine(root,"WinmmProxyMaker-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)); Directory.CreateDirectory(backup);
        var record=new XElement("Installation",new XAttribute("Game",game),new XAttribute("Backup",backup),new XAttribute("State","pending"));
        foreach(var f in m.Elements("File")) {
            string name=(string)f.Attribute("Name"), source=SafeChild(package,name), target=SafeChild(root,name);
            if(Hash(source)!=(string)f.Attribute("Hash")) throw new Exception("產物已變更："+name);
            bool existed=File.Exists(target); var r=new XElement("File",new XAttribute("Name",name),new XAttribute("InstalledHash",Hash(source)),new XAttribute("Existed",existed));
            if(existed) { File.Copy(target,Path.Combine(backup,name)); r.Add(new XAttribute("OriginalHash",Hash(target))); }
            record.Add(r);
        }
        new XDocument(record).Save(marker);
        try {
            foreach(var f in record.Elements("File")) AtomicCopy(SafeChild(package,(string)f.Attribute("Name")),SafeChild(root,(string)f.Attribute("Name")));
            record.SetAttributeValue("State","complete"); new XDocument(record).Save(marker); log("安裝完成。備份："+backup);
        } catch { LegacyRestore(marker,log); throw; }
    }
    public static void Restore(string marker,Action<string> log) {
        Integration.Restore(marker,log);
    }
    public static void LegacyRestore(string marker,Action<string> log) {
        var r=XDocument.Load(marker).Root; string game=(string)r.Attribute("Game"), root=Path.GetDirectoryName(game), backup=(string)r.Attribute("Backup"); Stopped(game);
        string expected=Path.Combine(root,"WinmmProxyMaker-backups")+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(backup).StartsWith(expected,StringComparison.OrdinalIgnoreCase)) throw new Exception("備份路徑無效");
        foreach(var f in r.Elements("File")) {
            string name=(string)f.Attribute("Name"), target=SafeChild(root,name); bool existed=(bool)f.Attribute("Existed");
            if(File.Exists(target) && Hash(target)!=(string)f.Attribute("InstalledHash") && (!existed || Hash(target)!=(string)f.Attribute("OriginalHash"))) throw new Exception(name+" 在安裝後已被修改，停止回復以保留檔案");
            if(existed && Hash(SafeChild(backup,name))!=(string)f.Attribute("OriginalHash")) throw new Exception("備份已變更："+name);
        }
        foreach(var f in r.Elements("File")) { string name=(string)f.Attribute("Name"), target=SafeChild(root,name); if((bool)f.Attribute("Existed")) AtomicCopy(SafeChild(backup,name),target); else if(File.Exists(target)) File.Delete(target); }
        File.Copy(marker,Path.Combine(backup,"installation.xml"),true); File.Delete(marker); log("回復完成。備份紀錄保留於："+backup);
    }
}
static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length==0){GuiBootstrap.EnableDpi();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());return 0;}
        try {
            if(args[0]=="analyze" && args.Length==2){var a=Core.AnalyzeGame(args[1]);Console.WriteLine(Core.Arch(a.Game));foreach(var c in a.Candidates)Console.WriteLine(c);return 0;}
            if(args[0]=="status" && args.Length==2){string reason;bool good=Integration.Loaded(args[1],out reason);Console.WriteLine(reason);return good?0:1;}
            if(args[0]=="apply" && args.Length==3){Integration.ApplyPreset(args[1],FontPreset.Read(XDocument.Load(args[2]).Root),Console.WriteLine);return 0;}
            if(args[0]=="analyze" && args.Length==3) {var a=Core.Analyze(args[1],args[2]);Console.WriteLine(Core.Arch(a.Game));foreach(var c in a.Candidates)Console.WriteLine(c);return 0;}
            if(args[0]=="build" && args.Length>=5) {string placement="root",mode="bak";bool early=false;FontPreset preset=null;for(int i=5;i<args.Length;i++){if(args[i]=="--early")early=true;else if(args[i]=="--placement"&&i+1<args.Length)placement=args[++i];else if(args[i]=="--original"&&i+1<args.Length)mode=args[++i];else if(args[i]=="--preset"&&i+1<args.Length)preset=FontPreset.Read(XDocument.Load(args[++i]).Root);else throw new Exception("未知建置選項："+args[i]);}var a=Core.Analyze(args[1],args[2]);var c=a.Candidates.FirstOrDefault(x=>string.Equals(x.Name,args[3],StringComparison.OrdinalIgnoreCase));if(c==null)throw new Exception("指定入口不在支援的靜態匯入候選中");Console.WriteLine(Core.Build(a,c,args[4],early,Console.WriteLine,placement,mode,preset));return 0;}
            if(args[0]=="install"&&args.Length==2){Core.Install(Path.GetFullPath(args[1]),Console.WriteLine);return 0;}
            if(args[0]=="restore"&&args.Length==2){Core.Restore(Path.GetFullPath(args[1]),Console.WriteLine);return 0;}
            Console.WriteLine("PatchFontBridge.exe\nanalyze <game.exe> <patch.dll>\nbuild <game.exe> <patch.dll> <host.dll> <output-folder> [--early] [--placement root|dll|patch|translation] [--original bak|delete] [--preset preset.xml]\ninstall <package-folder>\nrestore <game-folder\\WinmmProxyMaker-install.xml>");return 2;
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
}

