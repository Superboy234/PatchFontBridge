using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace WinmmProxyMaker {
static class Integration {
    public static string Marker(string game){string root=Path.GetDirectoryName(Path.GetFullPath(game)),old=Path.Combine(root,"WinmmProxyMaker-install.xml");return File.Exists(old)?old:Path.Combine(root,".WinmmProxyMaker","install.xml");}
    public static byte[] FontBinary(string arch) {
        using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("sfh."+arch)) {
            if(s==null)throw new Exception("套件缺少 SimpleFontHook："+arch);
            using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}
        }
    }
    static string Full(string p){return Path.GetFullPath(p);}
    static bool Same(string a,string b){return string.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase);}
    static string Child(string root,string relative) {
        if(Path.IsPathRooted(relative)||string.IsNullOrWhiteSpace(relative))throw new Exception("清單路徑無效");
        string p=Full(Path.Combine(root,relative));
        if(!p.StartsWith(Full(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new Exception("清單超出安裝目錄");
        return p;
    }
    static void Stopped(string game) {
        int own=Process.GetCurrentProcess().Id;
        foreach(var p in Process.GetProcesses())using(p){if(p.Id==own)continue;string name=null;try{name=p.MainModule.FileName;}catch(System.ComponentModel.Win32Exception){}catch(NotSupportedException){}catch(InvalidOperationException){}
            if(name!=null && string.Equals(Path.GetDirectoryName(name),Path.GetDirectoryName(game),StringComparison.OrdinalIgnoreCase))throw new Exception("請先關閉遊戲目錄中的程式");}
    }
    static string Digest(byte[] b){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
    static void Atomic(byte[] bytes,string dest) {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)); string temp=dest+".wpm-"+Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temp,bytes);
        try{if(File.Exists(dest))File.Replace(temp,dest,null);else File.Move(temp,dest);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static byte[] Configure(byte[] bytes) {
        Encoding encoding=new UTF8Encoding(false);int offset=0;
        if(bytes!=null){
            if(bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191){encoding=new UTF8Encoding(true);offset=3;}
            else if(bytes.Length>=2&&bytes[0]==255&&bytes[1]==254){encoding=Encoding.Unicode;offset=2;}
            else if(bytes.Length>=2&&bytes[0]==254&&bytes[1]==255){encoding=Encoding.BigEndianUnicode;offset=2;}
            else {try{new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){encoding=Encoding.Default;}}
        }
        string text=bytes==null?"[FontHook]\r\nEnableFontHook=1\r\n":encoding.GetString(bytes,offset,bytes.Length-offset);
        string newline=text.Contains("\r\n")?"\r\n":"\n";
        var lines=text.Replace("\r\n","\n").Split('\n').ToList();
        bool inSection=false,foundSection=false,foundKey=false;int insert=-1;
        for(int i=0;i<lines.Count;i++) {
            string trim=lines[i].Trim();
            if(trim.StartsWith("[")&&trim.EndsWith("]")) {
                if(inSection && insert<0)insert=i;
                inSection=trim.Equals("[FontHook]",StringComparison.OrdinalIgnoreCase);
                if(inSection)foundSection=true;
            }
            // SimpleFontHook's parser ignores section headers; normalize every duplicate.
            if(System.Text.RegularExpressions.Regex.IsMatch(trim,@"^PickerShowOnStartup\s*=",System.Text.RegularExpressions.RegexOptions.IgnoreCase)) {lines[i]="PickerShowOnStartup=0";foundKey=true;}
        }
        if(!foundSection){if(lines.Count>0&&lines[lines.Count-1]!="")lines.Add("");lines.Add("[FontHook]");lines.Add("PickerShowOnStartup=0");}
        else if(!foundKey){lines.Insert(insert<0?lines.Count:insert,"PickerShowOnStartup=0");}
        // SFH reads UTF-8 itself rather than the Windows INI Unicode parser.
        lines=lines.Where(l=>!System.Text.RegularExpressions.Regex.IsMatch(l.Trim(),@"^PickerRequireGameWindow\s*=",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();lines.Add("PickerRequireGameWindow=1");
        return new UTF8Encoding(false).GetBytes(string.Join(newline,lines).TrimEnd('\r','\n')+newline);
    }
    public static string Preview(string package) {
        var m=XDocument.Load(Path.Combine(package,"package.xml")).Root;
        string root=Path.GetDirectoryName((string)m.Attribute("Game"));
        var lines=new List<string>{"安裝位置："+root,""};
        foreach(var f in m.Elements("File"))lines.Add("＋ "+(string)f.Attribute("Path"));
        lines.Add("＋ FontHook.ini：PickerShowOnStartup=0");
        string source=(string)m.Attribute("SourcePatch"),mode=(string)m.Attribute("OriginalMode");
        lines.Add("");lines.Add("原漢化 DLL："+source);lines.Add(mode=="delete"?"清除原 DLL（仍保留回復備份）":"原 DLL 與被替換的 DLL 另存 .bak");
        if(!string.IsNullOrEmpty((string)m.Attribute("Folder")))lines.Add("漢化主代理位於 "+(string)m.Attribute("Folder")+"；根目錄包含載入橋接。");
        if(m.Element("FontPreset")!=null){var p=FontPreset.Read(m.Element("FontPreset"));lines.Add("字型："+p.Face+"；快捷鍵："+p.HotkeyText());}
        return string.Join(Environment.NewLine,lines);
    }
    public static void Install(string package,Action<string> log) {
        var m=XDocument.Load(Path.Combine(package,"package.xml")).Root;
        if((string)m.Attribute("Format")!="2")throw new Exception("請使用 1.1 版重新建立整合代理");
        string game=Full((string)m.Attribute("Game")),root=Path.GetDirectoryName(game),sourcePatch=Full((string)m.Attribute("SourcePatch"));Stopped(game);
        if(Core.Hash(game)!=(string)m.Attribute("GameHash"))throw new Exception("遊戲 EXE 已變更，請重新分析");
        if(!File.Exists(sourcePatch)||Core.Hash(sourcePatch)!=(string)m.Attribute("SourcePatchHash"))throw new Exception("原始漢化 DLL 已變更或不存在，請重新分析");
        string marker=Marker(game);if(File.Exists(marker))throw new Exception("此遊戲已安裝；可在字型頁套用設定，或先還原再重新安裝");
        var data=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in m.Elements("File")){string name=(string)f.Attribute("Path"),file=Child(package,name),target=Child(root,name);byte[] b=File.ReadAllBytes(file);if(Digest(b)!=(string)f.Attribute("Hash"))throw new Exception("產物已变更："+name);data.Add(target,b);}
        string ini=Path.Combine(root,"FontHook.ini");byte[] previous=File.Exists(ini)?File.ReadAllBytes(ini):null;data[ini]=m.Element("FontPreset")==null?Configure(previous):FontPreset.Read(m.Element("FontPreset")).Apply(previous);
        bool retire=!data.ContainsKey(sourcePatch)&&sourcePatch.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase);
        var paths=data.Keys.ToList();if(retire)paths.Add(sourcePatch);
        string backup=Path.Combine(root,".WinmmProxyMaker","backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(backup);
        var record=new XElement("Installation",new XAttribute("Format","2"),new XAttribute("Game",game),new XAttribute("SourcePatch",sourcePatch),new XAttribute("Backup",backup),new XAttribute("State","pending"),new XAttribute("Version","1.4"),new XAttribute("PrimaryProxy",(string)m.Attribute("PrimaryProxy")??(string)m.Attribute("Host")),new XAttribute("Payload",(string)m.Attribute("Payload")),new XAttribute("InstalledAt",DateTime.UtcNow.ToString("o")));
        int id=0;
        foreach(string target in paths) {
            bool exists=File.Exists(target);byte[] newData;bool hasNew=data.TryGetValue(target,out newData);
            var f=new XElement("File",new XAttribute("Target",target),new XAttribute("BackupFile",(id++)+".bin"),new XAttribute("Existed",exists),new XAttribute("Retired",!hasNew));
            if(hasNew)f.Add(new XAttribute("InstalledHash",Digest(newData)));
            if(exists){byte[] old=File.ReadAllBytes(target);File.WriteAllBytes(Path.Combine(backup,(string)f.Attribute("BackupFile")),old);f.Add(new XAttribute("OriginalHash",Digest(old)));
                if((string)m.Attribute("OriginalMode")=="bak"&&target.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)&&(!hasNew||Digest(old)!=Digest(newData))){
                    string bak=target+".bak";if(File.Exists(bak))bak=target+"."+Guid.NewGuid().ToString("N").Substring(0,8)+".bak";f.Add(new XAttribute("VisibleBackup",bak));
                }
            }
            record.Add(f);
        }
        var dirs=data.Keys.Select(Path.GetDirectoryName).Where(d=>!Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();foreach(string d in dirs)record.Add(new XElement("CreatedDirectory",new XAttribute("Path",d)));
        new XDocument(record).Save(marker);
        try {
            foreach(var f in record.Elements("File"))if(f.Attribute("VisibleBackup")!=null)File.Copy(Path.Combine(backup,(string)f.Attribute("BackupFile")),(string)f.Attribute("VisibleBackup"));
            // Payload and configuration are available before any game can launch.
            foreach(var pair in data)Atomic(pair.Value,pair.Key);
            if(retire)File.Delete(sourcePatch);
            record.SetAttributeValue("State","complete");new XDocument(record).Save(marker);
            log("安裝完成：漢化代理 + SimpleFontHook "+(string)m.Attribute("Architecture")+"\nPickerShowOnStartup=0\n備份："+backup);
        }catch(Exception e){try{Restore(marker,log);}catch(Exception rollback){throw new Exception(e.Message+"\n自動回復未完成："+rollback.Message+"\n紀錄："+marker);}throw;}
    }
    public static void Restore(string marker,Action<string> log) {
        var r=XDocument.Load(marker).Root;
        if((string)r.Attribute("Format")!="2"){Core.LegacyRestore(marker,log);return;}
        string game=Full((string)r.Attribute("Game")),root=Path.GetDirectoryName(game),source=Full((string)r.Attribute("SourcePatch")),backup=Full((string)r.Attribute("Backup"));Stopped(game);
        bool markerValid=Same(marker,Path.Combine(root,"WinmmProxyMaker-install.xml"))||Same(marker,Path.Combine(root,".WinmmProxyMaker","install.xml"));
        bool backupValid=backup.StartsWith(Path.Combine(root,"WinmmProxyMaker-backups")+"\\",StringComparison.OrdinalIgnoreCase)||backup.StartsWith(Path.Combine(root,".WinmmProxyMaker","backups")+"\\",StringComparison.OrdinalIgnoreCase);
        if(!markerValid||!backupValid)throw new Exception("安裝紀錄路徑無效");
        bool pending=(string)r.Attribute("State")=="pending";
        foreach(var f in r.Elements("File")) {
            string target=Full((string)f.Attribute("Target"));bool existed=(bool)f.Attribute("Existed"),retired=(bool)f.Attribute("Retired");
            if(!target.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&!Same(target,source))throw new Exception("回復目標超出紀錄範圍");
            if(File.Exists(target)){string hash=Core.Hash(target);bool original=existed&&hash==(string)f.Attribute("OriginalHash"),installed=!retired&&hash==(string)f.Attribute("InstalledHash");if(!installed&&!original){if(Same(target,Path.Combine(root,"FontHook.ini"))){string saved=Path.Combine(backup,"user-font-settings-"+DateTime.Now.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".ini");File.Copy(target,saved);f.SetAttributeValue("InstalledHash",hash);installed=true;log("使用期間的字型設定已另行備份："+saved);}else throw new Exception(Path.GetFileName(target)+" 已被修改，停止回復以保留檔案");}if(retired&&!pending&&!original)throw new Exception("已移除的漢化 DLL 路徑出現新檔案");}
            if(existed&&Core.Hash(Child(backup,(string)f.Attribute("BackupFile")))!=(string)f.Attribute("OriginalHash"))throw new Exception("回復備份已被修改");
            var visible=f.Attribute("VisibleBackup");if(visible!=null){string bak=(string)visible;if(!bak.StartsWith(target+".",StringComparison.OrdinalIgnoreCase)||!bak.EndsWith(".bak",StringComparison.OrdinalIgnoreCase))throw new Exception("備份檔名無效");if(File.Exists(bak)&&Core.Hash(bak)!=(string)f.Attribute("OriginalHash"))throw new Exception(".bak 備份已被修改，停止回復");}
        }
        foreach(var f in r.Elements("File")){string target=(string)f.Attribute("Target");if((bool)f.Attribute("Existed"))Atomic(File.ReadAllBytes(Child(backup,(string)f.Attribute("BackupFile"))),target);else if(File.Exists(target))File.Delete(target);if(f.Attribute("VisibleBackup")!=null&&File.Exists((string)f.Attribute("VisibleBackup")))File.Delete((string)f.Attribute("VisibleBackup"));}
        foreach(var d in r.Elements("CreatedDirectory")){string dir=Full((string)d.Attribute("Path"));if(dir.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(dir)&&!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);}
        File.Copy(marker,Path.Combine(backup,"installation.xml"),true);File.Delete(marker);log("回復完成：原 DLL、字型設定與原漢化 DLL 已還原。");
    }
    public static bool Loaded(string game,out string reason){
        reason="尚未安裝";try{
            string marker=Marker(game);if(!File.Exists(marker))return false;var m=XDocument.Load(marker).Root;
            if((string)m.Attribute("Version")!="1.2"&&(string)m.Attribute("Version")!="1.3"&&(string)m.Attribute("Version")!="1.3.1"&&(string)m.Attribute("Version")!="1.4"){reason="舊版安裝，需更新代理以確認載入狀態";return false;}
            string root=Path.GetDirectoryName(Path.GetFullPath(game)),proxy=Child(root,(string)m.Attribute("PrimaryProxy")),log=proxy+".proxy.log";
            foreach(var f in m.Elements("File").Where(f=>!(bool)f.Attribute("Retired")&&((string)f.Attribute("Target")).EndsWith(".dll",StringComparison.OrdinalIgnoreCase))){string t=(string)f.Attribute("Target");if(!File.Exists(t)||Core.Hash(t)!=(string)f.Attribute("InstalledHash")){reason="DLL 已變更，需重新確認載入";return false;}}
            DateTime installed=DateTime.Parse((string)m.Attribute("InstalledAt"),null,System.Globalization.DateTimeStyles.RoundtripKind);
            if(!File.Exists(log)||File.GetLastWriteTimeUtc(log)<installed){reason="等待啟動遊戲確認載入";return false;}
            string line=File.ReadLines(log).LastOrDefault();bool good=line!=null&&line.Contains("payload="+(string)m.Attribute("Payload")+" ")&&System.Text.RegularExpressions.Regex.IsMatch(line,@"load_error=0\s+font_error=0(?:\s|$)");
            reason=good?"漢化與 SimpleFontHook 已成功載入":"載入未成功，請查看 Help／日誌";return good;
        }catch(Exception e){reason="無法確認："+e.Message;return false;}
    }
    public static void ApplyPreset(string game,FontPreset preset,Action<string> log){
        string reason;if(!Loaded(game,out reason))throw new Exception(reason);
        string marker=Marker(game);var m=XDocument.Load(marker).Root;string root=Path.GetDirectoryName(Path.GetFullPath(game)),ini=Path.Combine(root,"FontHook.ini");
        var f=m.Elements("File").FirstOrDefault(x=>Same((string)x.Attribute("Target"),ini));if(f==null)throw new Exception("安裝紀錄缺少字型設定");
        byte[] old=File.Exists(ini)?File.ReadAllBytes(ini):null,next=preset.Apply(old);string oldHash=(string)f.Attribute("InstalledHash");
        try{Atomic(next,ini);f.SetAttributeValue("InstalledHash",Digest(next));var previous=m.Element("FontPreset");if(previous!=null)previous.Remove();m.Add(preset.Xml());Atomic(new UTF8Encoding(false).GetBytes(new XDocument(m).ToString()),marker);}
        catch{if(old!=null)Atomic(old,ini);else if(File.Exists(ini))File.Delete(ini);f.SetAttributeValue("InstalledHash",oldHash);throw;}
        log("設定已分發："+Path.GetFileName(game)+"；下次啟動遊戲生效（"+preset.HotkeyText()+"）。");
    }
    public static void Upgrade(string game,Action createAndInstall,Action<string> log){
        string marker=Marker(game);if(!File.Exists(marker)){createAndInstall();return;}
        Stopped(game);var doc=XDocument.Load(marker).Root;if((string)doc.Attribute("Format")!="2")throw new Exception("舊安裝請先按還原，再安裝新版");
        byte[] markerBytes=File.ReadAllBytes(marker);string root=Path.GetDirectoryName(Path.GetFullPath(game)),source=Full((string)doc.Attribute("SourcePatch"));
        var snapshots=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in doc.Elements("File")){string target=Full((string)f.Attribute("Target"));if(!target.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&!Same(target,source))throw new Exception("更新目標無效");snapshots[target]=File.Exists(target)?File.ReadAllBytes(target):null;var b=f.Attribute("VisibleBackup");if(b!=null){string bak=(string)b;if(!bak.StartsWith(target+".",StringComparison.OrdinalIgnoreCase)||!bak.EndsWith(".bak",StringComparison.OrdinalIgnoreCase))throw new Exception("備份路徑無效");snapshots[bak]=File.Exists(bak)?File.ReadAllBytes(bak):null;}}
        Restore(marker,log);
        try{createAndInstall();}catch(Exception error){
            try{if(File.Exists(Marker(game)))Restore(Marker(game),log);foreach(var pair in snapshots){if(pair.Value==null){if(File.Exists(pair.Key))File.Delete(pair.Key);}else Atomic(pair.Value,pair.Key);}Atomic(markerBytes,marker);log("更新未完成，已還原上一版安裝。");}
            catch(Exception rollback){throw new Exception(error.Message+"\n上一版回復未完成："+rollback.Message);}
            throw;
        }
    }
}
}
