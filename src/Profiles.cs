using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;

namespace WinmmProxyMaker {
sealed class FontPreset {
    public string Face="Microsoft YaHei";
    public int Height=100,Width=100,Spacing=0,Charset=-1,Vk=121,Modifiers=3;
    public bool Bold=false,OverrideFont=false;
    public void Validate(){if(Face.Length>31||Face.Contains("\n")||Face.Contains("\r"))throw new Exception("字型名稱最多 31 個字元");if(Height<50||Height>200||Width<50||Width>200||Spacing < -10||Spacing>30||Vk<1||Vk>255||(Modifiers&~15)!=0)throw new Exception("字型或快捷鍵設定超出支援範圍");if(Charset!=-1&&Charset!=128&&Charset!=134&&Charset!=136&&Charset!=1)throw new Exception("不支援的字集設定");}
    public XElement Xml(){Validate();return new XElement("FontPreset",new XAttribute("OverrideFont",OverrideFont),new XAttribute("Face",Face),new XAttribute("Height",Height),new XAttribute("Width",Width),new XAttribute("Spacing",Spacing),new XAttribute("Charset",Charset),new XAttribute("Bold",Bold),new XAttribute("Vk",Vk),new XAttribute("Modifiers",Modifiers));}
    public static FontPreset Read(XElement x){var p=new FontPreset();if(x==null)return p;p.OverrideFont=(bool?)x.Attribute("OverrideFont")??false;p.Face=(string)x.Attribute("Face")??p.Face;p.Height=(int?)x.Attribute("Height")??100;p.Width=(int?)x.Attribute("Width")??100;p.Spacing=(int?)x.Attribute("Spacing")??0;p.Charset=(int?)x.Attribute("Charset")??-1;p.Bold=(bool?)x.Attribute("Bold")??false;p.Vk=(int?)x.Attribute("Vk")??121;p.Modifiers=(int?)x.Attribute("Modifiers")??3;p.Validate();return p;}
    static Dictionary<string,string> Keys(byte[] b){var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(string line in Encoding.UTF8.GetString(Integration.Configure(b)).Replace("\r\n","\n").Split('\n')){int i=line.IndexOf('=');if(i>0&&!line.TrimStart().StartsWith(";"))d[line.Substring(0,i).Trim()]=line.Substring(i+1).Trim();}return d;}
    public static FontPreset FromIni(string file){var p=new FontPreset();if(!File.Exists(file))return p;var d=Keys(File.ReadAllBytes(file));Func<string,int,int> n=(k,f)=>{string s;int v;return d.TryGetValue(k,out s)&&int.TryParse(s,out v)?v:f;};string face;if(d.TryGetValue("FontNameW",out face))p.Face=face;p.Height=n("EnableFontHeightScale",0)!=0?n("FontHeightScale1000",1000)/10:100;p.Width=n("EnableFontWidthScale",0)!=0?n("FontWidthScale1000",1000)/10:100;p.Spacing=n("EnableFontCharSpacing",0)!=0?n("FontCharSpacing",0):0;p.Charset=n("EnableCharsetReplace",0)!=0?n("ForcedCharset",-1):-1;p.Bold=n("EnableFontWeight",0)!=0&&n("FontWeight",400)>=600;p.Vk=n("PickerHotkeyVK",121);p.Modifiers=n("PickerHotkeyModifiers",3);try{p.Validate();}catch{return new FontPreset();}return p;}
    public byte[] Apply(byte[] previous){Validate();string text=Encoding.UTF8.GetString(Integration.Configure(previous));var values=new Dictionary<string,string>{
      {"PickerShowOnStartup","0"},{"PickerHotkeyVK",Vk.ToString()},{"PickerHotkeyModifiers",Modifiers.ToString()},
      {"EnableFontHook","1"},{"EnableFaceNameReplace",Face.Length==0?"0":"1"},{"FontNameW",Face},{"FontNameA",Face},
      {"EnableFontHeightScale",Height==100?"0":"1"},{"FontHeightScale1000",(Height*10).ToString()},
      {"EnableFontWidthScale",Width==100?"0":"1"},{"FontWidthScale1000",(Width*10).ToString()},
      {"EnableFontCharSpacing",Spacing==0?"0":"1"},{"FontCharSpacing",Spacing.ToString()},
      {"EnableFontWeight",Bold?"1":"0"},{"FontWeight",Bold?"700":"400"},
      {"EnableCharsetReplace",Charset<0?"0":"1"}};
      if(Charset>=0)values["ForcedCharset"]=Charset.ToString();
      if(!OverrideFont)values=values.Where(x=>x.Key.StartsWith("Picker",StringComparison.Ordinal)).ToDictionary(x=>x.Key,x=>x.Value);
      foreach(var pair in values){string pattern=@"(?m)^\s*"+System.Text.RegularExpressions.Regex.Escape(pair.Key)+@"\s*=[^\r\n]*";if(System.Text.RegularExpressions.Regex.IsMatch(text,pattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase))text=System.Text.RegularExpressions.Regex.Replace(text,pattern,_=>pair.Key+"="+pair.Value,System.Text.RegularExpressions.RegexOptions.IgnoreCase);else text=text.TrimEnd('\r','\n')+"\r\n"+pair.Key+"="+pair.Value+"\r\n";}
      return new UTF8Encoding(false).GetBytes(text);
    }
    public string HotkeyText(){string s=(Modifiers&2)!=0?"Ctrl+":"";if((Modifiers&1)!=0)s+="Alt+";if((Modifiers&4)!=0)s+="Shift+";if((Modifiers&8)!=0)s+="Win+";return s+((System.Windows.Forms.Keys)Vk).ToString();}
}
sealed class Preferences {
    public bool Dark=true,Mini=false;public List<string> Games=new List<string>();
    static string FileName {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".WinmmProxyMaker","preferences.xml");}}
    public static Preferences Load(){try{var x=XDocument.Load(FileName).Root;return new Preferences{Dark=(bool?)x.Attribute("Dark")??true,Mini=(bool?)x.Attribute("Mini")??false,Games=x.Elements("Game").Select(e=>e.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList()};}catch{return new Preferences();}}
    public void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(FileName));new XDocument(new XElement("Preferences",new XAttribute("Dark",Dark),new XAttribute("Mini",Mini),Games.Distinct(StringComparer.OrdinalIgnoreCase).Select(g=>new XElement("Game",g)))).Save(FileName);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
}
