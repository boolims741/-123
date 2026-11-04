// Optional local installer and CLR dependency check. No game code is executed.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.10.1.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2", FrameworkDisplayName = ".NET Framework 4.7.2")]

namespace EastAsiaMap.Installation
{
    internal static class InstallAndCheck
    {
        private const string ModuleId = "EastAsiaMap_Trial";
        private const string OwnDll = "bin/Win64_Shipping_Client/EastAsiaMap.Runtime.dll";
        private static readonly string[] Allowed = {
            "SubModule.xml", "ModuleData/settlements.xml",
            "SceneObj/Main_map/atmosphere.xml", "SceneObj/Main_map/flora.bin",
            "SceneObj/Main_map/navmesh.bin", "SceneObj/Main_map/references.txt",
            "SceneObj/Main_map/scene.xscene", "SceneObj/Main_map/terrain.bin", OwnDll
        };
        private static readonly StringBuilder Details = new StringBuilder();
        private static string logPath;

        [DllImport("kernel32.dll", EntryPoint = "DeleteFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFileStream(string path);

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            string packageRoot = AppDomain.CurrentDomain.BaseDirectory;
            logPath = Path.Combine(packageRoot, "EastAsiaMap_install-check.log");
            try
            {
                Log("Loader-check helper; CLR=" + Environment.Version + "; x64=" + Environment.Is64BitProcess);
                if (!Environment.Is64BitProcess) throw new InvalidOperationException("64비트 설치 검사 도구가 필요합니다.");
                Dictionary<string, string> manifest = ReadManifest(Path.Combine(packageRoot, "payload.sha256"));
                string source = Path.Combine(packageRoot, ModuleId);
                Verify(source, manifest);
                RequireGameClosed();
                string game = SelectGameDirectory();
                if (game == null) return;
                string target = Path.Combine(game, "Modules", ModuleId);
                string sourceDll = LocalPath(source, OwnDll);
                UnblockVerifiedOwnDll(sourceDll);
                Verify(source, manifest);
                // A failing CLR probe must leave the current installation intact.
                Probe(game, sourceDll);
                InstallFiles(source, target, game, manifest);
                Verify(target, manifest);
                string dll = LocalPath(target, OwnDll);
                UnblockVerifiedOwnDll(dll);
                Verify(target, manifest);
                logPath = Path.Combine(target, "RuntimeDiagnostics", "loader-check.log");
                SaveLog();
                MessageBox.Show("설치와 DLL 형식·의존성 로드 검사를 통과했습니다.\n\n" + target +
                    "\n\n캠페인 실행은 이 검사에 포함되지 않습니다. 런처에서 새 샌드박스로 확인하세요.",
                    "EastAsiaMap 설치 검사 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                Log(Describe(error));
                SaveLog();
                string message = error.Message;
                if (message.Length > 1200) message = message.Substring(0, 1200);
                MessageBox.Show("설치/로드 검사를 완료하지 못했습니다.\n\n" + message +
                    "\n\n진단 기록: " + logPath, "EastAsiaMap 설치 검사", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void UnblockVerifiedOwnDll(string dll)
        {
            if (!DeleteFileStream(dll + ":Zone.Identifier"))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 2 && error != 3) throw new Win32Exception(error, "자체 DLL의 파일 차단 해제를 완료하지 못했습니다.");
                Log("Own DLL zone stream absent.");
            }
            else Log("Own DLL zone stream removed after SHA256 verification.");
        }

        internal static Dictionary<string, string> ReadManifest(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                if (line.Length < 67 || line.Substring(64, 2) != "  " ||
                    line.Substring(0, 64).Any(c => !Uri.IsHexDigit(c)))
                    throw new InvalidDataException("파일 검사 목록 형식이 잘못되었습니다.");
                string prefix = ModuleId + "/";
                string entry = line.Substring(66);
                if (!entry.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("잘못된 모듈 경로입니다.");
                string relative = entry.Substring(prefix.Length);
                if (!Allowed.Contains(relative, StringComparer.Ordinal) || result.ContainsKey(relative))
                    throw new InvalidDataException("허용되지 않거나 중복된 설치 파일입니다: " + relative);
                result.Add(relative, line.Substring(0, 64));
            }
            if (result.Count != Allowed.Length) throw new InvalidDataException("설치 파일 9개의 검사 정보가 모두 필요합니다.");
            return result;
        }

        private static string LocalPath(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("모듈 밖의 경로는 설치할 수 없습니다.");
            return full;
        }

        internal static void Verify(string root, Dictionary<string, string> manifest)
        {
            foreach (var item in manifest)
            {
                string file = LocalPath(root, item.Key);
                string check = file;
                while (!String.Equals(Path.GetFullPath(check).TrimEnd(Path.DirectorySeparatorChar),
                                      Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결/리디렉션 파일은 설치할 수 없습니다: " + item.Key);
                    check = Path.GetDirectoryName(check);
                }
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 모듈 폴더는 설치할 수 없습니다.");
                string actual;
                using (var hash = SHA256.Create())
                using (var stream = File.OpenRead(file)) actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                if (!String.Equals(actual, item.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("배포 파일과 일치하지 않습니다. 설치를 중단했습니다: " + item.Key);
            }
            Log("Verified all nine payload files: " + root);
        }

        internal static void InstallFiles(string source, string target, string game, Dictionary<string, string> manifest)
        {
            InstallFilesCore(source, target, game, manifest, delegate(string from, string to) { File.Copy(from, to, false); });
        }

        internal static void InstallFilesCore(string source, string target, string game, Dictionary<string, string> manifest, Action<string, string> copy)
        {
            if (String.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                              Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            { Log("Payload already at target; verify/unblock/probe only."); return; }
            Verify(source, manifest);
            string fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(source).StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
                throw new IOException("압축을 푼 폴더가 설치 대상 안에 있습니다. 다운로드 폴더 등 게임 밖에서 실행하세요.");
            if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("연결된 기존 모듈 폴더는 이동하지 않습니다.");
            string backupBase = Path.Combine(game, "EastAsiaMap_Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string backup = Path.Combine(backupBase, ModuleId);
            bool previousMoved = false;
            bool copyStarted = false;
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.CreateDirectory(backupBase);
                    Directory.Move(target, backup);
                    previousMoved = true;
                    Log("Previous module preserved outside Modules: " + backup);
                }
                copyStarted = true;
                foreach (var item in manifest)
                {
                    string destination = LocalPath(target, item.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    copy(LocalPath(source, item.Key), destination);
                }
                Verify(target, manifest);
            }
            catch
            {
                // Preserve even the failed install rather than deleting files.
                if (copyStarted && Directory.Exists(target))
                {
                    Directory.CreateDirectory(backupBase);
                    Directory.Move(target, Path.Combine(backupBase, "failed-install-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
                }
                if (previousMoved) Directory.Move(backup, target);
                Log("Copy failed; previous module restored when present. Backup location: " + backupBase);
                throw;
            }
        }

        private static void RequireGameClosed()
        {
            foreach (Process process in Process.GetProcesses())
                using (process)
                {
                    string name;
                    try { name = process.ProcessName; }
                    catch (InvalidOperationException) { continue; }
                    catch (Win32Exception) { continue; }
                    if (name.StartsWith("Bannerlord", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("TaleWorlds.MountAndBlade.Launcher", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("배너로드 게임과 런처를 종료한 뒤 이 도구를 다시 실행하세요.");
                }
        }

        private static bool IsGame(string path)
        {
            return Directory.Exists(Path.Combine(path, "Modules", "Native")) &&
                   File.Exists(Path.Combine(path, "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.dll"));
        }

        private static string SelectGameDirectory()
        {
            string known = @"D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord";
            if (IsGame(known)) return known;
            using (var dialog = new FolderBrowserDialog { Description = "배너로드 설치 폴더를 선택하세요 (Modules의 상위 폴더)", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return null;
                string selected = dialog.SelectedPath;
                if (String.Equals(Path.GetFileName(selected), "Modules", StringComparison.OrdinalIgnoreCase)) selected = Path.GetDirectoryName(selected);
                if (!IsGame(selected)) throw new DirectoryNotFoundException("선택한 폴더에 배너로드 본체 DLL과 Modules/Native가 없습니다.");
                return selected;
            }
        }

        private static void Probe(string game, string ownDll)
        {
            string[] directories = { Path.Combine(game, "bin", "Win64_Shipping_Client"),
                Path.Combine(game, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client"),
                Path.Combine(game, "Modules", "SandBox", "bin", "Win64_Shipping_Client"),
                Path.Combine(game, "Modules", "SandBoxCore", "bin", "Win64_Shipping_Client"),
                Path.Combine(game, "Modules", "Native", "bin", "Win64_Shipping_Client") };
            ResolveEventHandler resolver = delegate(object sender, ResolveEventArgs args)
            {
                var requested = new AssemblyName(args.Name);
                if (requested.Name != Path.GetFileName(requested.Name)) return null;
                foreach (string directory in directories)
                {
                    string file = Path.Combine(directory, requested.Name + ".dll");
                    if (!File.Exists(file)) continue;
                    var identity = AssemblyName.GetAssemblyName(file);
                    if (!String.Equals(identity.FullName, requested.FullName, StringComparison.OrdinalIgnoreCase))
                    { Log("Dependency identity mismatch: requested=" + requested.FullName + "; found=" + identity.FullName); continue; }
                    Log("Read-only dependency resolution: " + identity.FullName);
                    return Assembly.LoadFrom(file);
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try
            {
                Assembly assembly = Assembly.LoadFrom(ownDll);
                Type[] types = assembly.GetTypes();
                Type module = types.SingleOrDefault(t => t.FullName == "EastAsiaMap.Runtime.EastAsiaMapSubModule");
                if (module == null || !module.IsPublic || module.IsAbstract || module.BaseType == null ||
                    module.BaseType.FullName != "TaleWorlds.MountAndBlade.MBSubModuleBase")
                    throw new TypeLoadException("등록된 게임 모듈 클래스를 올바르게 로드하지 못했습니다.");
                Log("PASS Assembly.LoadFrom and GetTypes. Module type=" + module.FullName + "; type count=" + types.Length);
                Log("No game module instantiated; campaign execution is not verified.");
            }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
        }

        private static string Describe(Exception error)
        {
            string result = error.ToString() + "\nHRESULT=0x" + error.HResult.ToString("X8");
            var load = error as ReflectionTypeLoadException;
            if (load != null) foreach (Exception detail in load.LoaderExceptions) if (detail != null) result += "\nLOADER: " + Describe(detail);
            return result;
        }
        private static void Log(string message) { Details.AppendLine(DateTime.UtcNow.ToString("o") + " " + message); }
        private static void SaveLog()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllText(logPath, Details.ToString(), new UTF8Encoding(false)); }
            catch { logPath = Path.Combine(Path.GetTempPath(), "EastAsiaMap_install-check.log"); try { File.WriteAllText(logPath, Details.ToString(), new UTF8Encoding(false)); } catch { } }
        }
    }
}
