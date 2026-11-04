// UI stubs let the real install core execute on the build host. No Windows CLR
// load, NTFS zone operation or Bannerlord native execution is claimed here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using EastAsiaMap.Installation;

namespace System.Windows.Forms {
    public enum DialogResult { Cancel, OK }
    public enum MessageBoxButtons { OK }
    public enum MessageBoxIcon { Information, Error }
    public static class Application { public static void EnableVisualStyles() { } }
    public static class MessageBox { public static void Show(string a, string b, MessageBoxButtons c, MessageBoxIcon d) { } }
    public class FolderBrowserDialog : IDisposable {
        public string Description { get; set; }
        public bool ShowNewFolderButton { get; set; }
        public string SelectedPath { get; set; }
        public DialogResult ShowDialog() { return DialogResult.Cancel; }
        public void Dispose() { }
    }
}

public static class InstallerHarness {
    private static readonly string[] Names = {
        "SubModule.xml", "ModuleData/settlements.xml", "SceneObj/Main_map/atmosphere.xml",
        "SceneObj/Main_map/flora.bin", "SceneObj/Main_map/navmesh.bin", "SceneObj/Main_map/references.txt",
        "SceneObj/Main_map/scene.xscene", "SceneObj/Main_map/terrain.bin", "bin/Win64_Shipping_Client/EastAsiaMap.Runtime.dll"
    };
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Fails(Action action) { bool failed=false; try { action(); } catch (IOException) { failed=true; } catch (InvalidDataException) { failed=true; } Check(failed,"Expected IO/data failure"); }
    private static string Make(string root, out Dictionary<string,string> manifest) {
        string source=Path.Combine(root,"package","EastAsiaMap_Trial");
        manifest=new Dictionary<string,string>();
        foreach(string n in Names) {
            string f=Path.Combine(source,n);Directory.CreateDirectory(Path.GetDirectoryName(f));File.WriteAllText(f,"payload:"+n);
            using(var h=SHA256.Create()) manifest[n]=BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(f))).Replace("-","");
        }
        return source;
    }
    public static string[] Run(string root) {
        Directory.CreateDirectory(root);var passed=new List<string>();
        Dictionary<string,string> manifest;
        string source=Make(root,out manifest),game=Path.Combine(root,"game"),target=Path.Combine(game,"Modules","EastAsiaMap_Trial");
        Directory.CreateDirectory(Path.Combine(game,"Modules","Native"));File.WriteAllText(Path.Combine(game,"Modules","Native","untouched"),"native");
        string mf=Path.Combine(root,"payload.sha256");
        File.WriteAllLines(mf,manifest.Select(x=>x.Value+"  EastAsiaMap_Trial/"+x.Key));
        var parsed=InstallAndCheck.ReadManifest(mf);InstallAndCheck.Verify(source,parsed);Check(parsed.Count==9,"nine files");
        passed.Add("valid manifest and full payload verification");
        File.AppendAllText(mf,manifest.First().Value+"  EastAsiaMap_Trial/../outside\n");Fails(()=>InstallAndCheck.ReadManifest(mf));
        passed.Add("path traversal or extra manifest entry rejected");
        File.WriteAllLines(mf,manifest.Take(8).Select(x=>x.Value+"  EastAsiaMap_Trial/"+x.Key));Fails(()=>InstallAndCheck.ReadManifest(mf));
        passed.Add("incomplete manifest rejected");
        string modified=Path.Combine(source,Names[0]);File.AppendAllText(modified,"changed");Fails(()=>InstallAndCheck.Verify(source,manifest));
        File.WriteAllText(modified,"payload:"+Names[0]);
        passed.Add("altered payload rejected before installation");
        Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"user-work.txt"),"preserve");
        InstallAndCheck.InstallFiles(source,target,game,manifest);InstallAndCheck.Verify(target,manifest);
        Check(Directory.GetFiles(Path.Combine(game,"EastAsiaMap_Backups"),"user-work.txt",SearchOption.AllDirectories).Length==1,"old work preserved");
        Check(File.ReadAllText(Path.Combine(game,"Modules","Native","untouched"))=="native","native unchanged");
        passed.Add("whole old module backed up outside Modules and native files untouched");
        File.WriteAllText(Path.Combine(target,"marker.txt"),"original");int copies=0;
        Fails(()=>InstallAndCheck.InstallFilesCore(source,target,game,manifest,delegate(string from,string to){if(++copies==3)throw new IOException("simulated disk failure");File.Copy(from,to,false);}));
        Check(File.ReadAllText(Path.Combine(target,"marker.txt"))=="original","previous module restored");
        Check(Directory.GetDirectories(Path.Combine(game,"EastAsiaMap_Backups"),"failed-install-*",SearchOption.AllDirectories).Length==1,"failed data preserved");
        passed.Add("partial-copy failure restores old module and preserves failed copy");
        string blocked=Path.Combine(root,"blocked-game"),blockedTarget=Path.Combine(blocked,"Modules","EastAsiaMap_Trial");
        Directory.CreateDirectory(blockedTarget);File.WriteAllText(Path.Combine(blockedTarget,"original"),"untouched");
        File.WriteAllText(Path.Combine(blocked,"EastAsiaMap_Backups"),"file blocks backup directory");
        Fails(()=>InstallAndCheck.InstallFiles(source,blockedTarget,blocked,manifest));
        Check(File.Exists(Path.Combine(blockedTarget,"original")),"pre-copy failure must not move original");
        passed.Add("backup creation failure leaves existing installation in place");
        string nested=Path.Combine(source,"ModuleData");
        Fails(()=>InstallAndCheck.InstallFiles(source,Directory.GetParent(source).FullName,game,manifest));
        Check(File.Exists(Path.Combine(source,Names[0])),"source remains in place");
        passed.Add("payload inside destination rejected without moving files");
        int backups=Directory.GetDirectories(Path.Combine(game,"EastAsiaMap_Backups")).Length;
        InstallAndCheck.InstallFiles(source,source,game,manifest);
        Check(Directory.GetDirectories(Path.Combine(game,"EastAsiaMap_Backups")).Length==backups,"no backup for same source");
        passed.Add("already-installed source is not moved or overwritten");
        return passed.ToArray();
    }
}
