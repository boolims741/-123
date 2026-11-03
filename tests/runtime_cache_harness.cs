// Managed behavioral tests for the actual runtime source, compiled with stubs.
// These tests cannot validate native Bannerlord lifecycle or path generation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using EastAsiaMap.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Map.DistanceCache;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TaleWorlds.Core
{
    public class GameModel { }
    public interface IGameStarter { }
    public class Game { public object GameType; }
}
namespace TaleWorlds.Library
{
    public static class BasePath { public static string Name; }
    public class MBReadOnlyList<T> : List<T> { }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public class MobileParty { public enum NavigationType { Default, Naval, All } }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public class Settlement { public string Marker; }
}
namespace TaleWorlds.CampaignSystem
{
    public class Campaign { public static Campaign Current; public object MapSceneWrapper; }
    public class CampaignGameStarter : IGameStarter
    {
        public readonly List<GameModel> Models = new List<GameModel>();
        public void AddModel(GameModel model) { Models.Add(model); }
    }
}
namespace TaleWorlds.CampaignSystem.ComponentInterfaces
{
    public abstract class MapDistanceModel : GameModel
    {
        public interface INavigationCache
        {
            float MaximumDistanceBetweenTwoConnectedSettlements { get; }
            float GetSettlementToSettlementDistanceWithLandRatio(Settlement a, bool portA, Settlement b, bool portB, out float ratio);
            MBReadOnlyList<Settlement> GetNeighbors(Settlement settlement);
            Settlement GetClosestSettlementToFaceIndex(int face, out bool portUsed);
            void FinalizeInitialization();
        }
    }
}
namespace TaleWorlds.MountAndBlade
{
    public class MBSubModuleBase
    {
        protected virtual void OnSubModuleLoad() { }
        protected virtual void OnGameStart(Game game, IGameStarter starter) { }
    }
}
namespace NavalDLC.GameComponents
{
    public class NavalDLCMapDistanceModel : MapDistanceModel
    {
        public readonly Dictionary<MobileParty.NavigationType, INavigationCache> Registered = new Dictionary<MobileParty.NavigationType, INavigationCache>();
        public virtual void RegisterDistanceCache(MobileParty.NavigationType capability, INavigationCache cache) { Registered[capability] = cache; }
    }
}
namespace TaleWorlds.CampaignSystem.Map.DistanceCache
{
    public class SandBoxNavigationCache : MapDistanceModel.INavigationCache
    {
        public static readonly List<SandBoxNavigationCache> Instances = new List<SandBoxNavigationCache>();
        public static Action<SandBoxNavigationCache> DuringGeneration;
        public static Action<SandBoxNavigationCache> DuringFinalization;
        public readonly MobileParty.NavigationType Capability;
        public readonly MBReadOnlyList<Settlement> NeighborResult = new MBReadOnlyList<Settlement>();
        public readonly Settlement ClosestResult = new Settlement { Marker = "closest" };
        public int GenerateCount, FinalizeCount, LastFace;
        public Settlement LastA, LastB, LastNeighbor;
        public bool LastPortA, LastPortB;
        public Exception DistanceFailure;
        public SandBoxNavigationCache(MobileParty.NavigationType capability) { Capability = capability; Instances.Add(this); }
        public void GenerateCacheData() { GenerateCount++; if (DuringGeneration != null) DuringGeneration(this); }
        public void FinalizeInitialization() { FinalizeCount++; if (DuringFinalization != null) DuringFinalization(this); }
        public float MaximumDistanceBetweenTwoConnectedSettlements { get { return 1000 + (int)Capability; } }
        public float GetSettlementToSettlementDistanceWithLandRatio(Settlement a, bool portA, Settlement b, bool portB, out float ratio)
        {
            if (DistanceFailure != null) throw DistanceFailure;
            LastA = a; LastB = b; LastPortA = portA; LastPortB = portB;
            ratio = .375f; return 100 + (int)Capability;
        }
        public MBReadOnlyList<Settlement> GetNeighbors(Settlement settlement) { LastNeighbor = settlement; return NeighborResult; }
        public Settlement GetClosestSettlementToFaceIndex(int face, out bool portUsed) { LastFace = face; portUsed = face == 43; return ClosestResult; }
        public static void Reset() { Instances.Clear(); DuringGeneration = null; DuringFinalization = null; }
    }
}
public static class RuntimeCacheHarness
{
    private sealed class PoisonStockCache : MapDistanceModel.INavigationCache
    {
        public int Calls;
        private Exception Poison() { Calls++; return new Exception("The stock map cache must never be queried."); }
        public float MaximumDistanceBetweenTwoConnectedSettlements { get { throw Poison(); } }
        public float GetSettlementToSettlementDistanceWithLandRatio(Settlement a, bool pa, Settlement b, bool pb, out float ratio) { throw Poison(); }
        public MBReadOnlyList<Settlement> GetNeighbors(Settlement a) { throw Poison(); }
        public Settlement GetClosestSettlementToFaceIndex(int face, out bool port) { throw Poison(); }
        public void FinalizeInitialization() { throw Poison(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("HARNESS ASSERTION: " + message); }
    private static Exception Failure(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new Exception("HARNESS ASSERTION: expected exception was swallowed");
    }
    private static void Reset()
    {
        SandBoxNavigationCache.Reset();
        Campaign.Current = new Campaign { MapSceneWrapper = new object() };
    }
    private static void Case(string name, Action action, List<string> passed)
    {
        Reset(); action(); passed.Add(name);
    }
    public static string[] Run(string logRoot)
    {
        Directory.CreateDirectory(logRoot); BasePath.Name = logRoot;
        List<string> passed = new List<string>();
        Case("campaign-only model registration", delegate
        {
            EastAsiaMapSubModule module = new EastAsiaMapSubModule();
            MethodInfo start = typeof(EastAsiaMapSubModule).GetMethod("OnGameStart", BindingFlags.Instance | BindingFlags.NonPublic);
            CampaignGameStarter starter = new CampaignGameStarter();
            start.Invoke(module, new object[] { null, starter });
            start.Invoke(module, new object[] { new Game { GameType = new object() }, starter });
            Check(starter.Models.Count == 0, "noncampaign registered a model");
            start.Invoke(module, new object[] { new Game { GameType = new Campaign() }, starter });
            Check(starter.Models.Count == 1 && starter.Models[0] is EastAsiaNavalMapDistanceModel, "campaign failed to register runtime model");
        }, passed);
        Case("stock cache bypass, capability isolation and deferred generation", delegate
        {
            EastAsiaNavalMapDistanceModel model = new EastAsiaNavalMapDistanceModel();
            PoisonStockCache stock = new PoisonStockCache();
            foreach (MobileParty.NavigationType capability in Enum.GetValues(typeof(MobileParty.NavigationType)))
            {
                model.RegisterDistanceCache(capability, stock);
                model.Registered[capability].FinalizeInitialization();
            }
            Check(SandBoxNavigationCache.Instances.Count == 0, "registration/finalization generated before a query");
            foreach (MobileParty.NavigationType capability in Enum.GetValues(typeof(MobileParty.NavigationType)))
                for (int n = 0; n < 4; n++) Check(model.Registered[capability].MaximumDistanceBetweenTwoConnectedSettlements == 1000 + (int)capability, "capability result mixed");
            Check(SandBoxNavigationCache.Instances.Count == 3, "capabilities shared or recreated cache");
            foreach (SandBoxNavigationCache cache in SandBoxNavigationCache.Instances)
                Check(cache.GenerateCount == 1 && cache.FinalizeCount == 1, "cache did not generate/finalize exactly once");
            Check(stock.Calls == 0, "stock cache used");
        }, passed);
        Case("reference, entrance flags and out-parameter passthrough", delegate
        {
            CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.All);
            Settlement a = new Settlement(), b = new Settlement(); float ratio; bool port;
            Check(proxy.GetSettlementToSettlementDistanceWithLandRatio(a, true, b, false, out ratio) == 102 && ratio == .375f, "distance/ratio replaced");
            SandBoxNavigationCache real = SandBoxNavigationCache.Instances[0];
            Check(ReferenceEquals(real.LastA, a) && ReferenceEquals(real.LastB, b) && real.LastPortA && !real.LastPortB, "settlement/entrance arguments changed");
            Check(ReferenceEquals(proxy.GetNeighbors(a), real.NeighborResult) && ReferenceEquals(real.LastNeighbor, a), "neighbors replaced");
            Check(ReferenceEquals(proxy.GetClosestSettlementToFaceIndex(43, out port), real.ClosestResult) && port && real.LastFace == 43, "closest face/port changed");
            proxy.GetClosestSettlementToFaceIndex(12, out port); Check(!port, "false out port not forwarded");
        }, passed);
        Case("native-query error is preserved", delegate
        {
            CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.Naval);
            float value = proxy.MaximumDistanceBetweenTwoConnectedSettlements;
            Exception sentinel = new ApplicationException("sentinel-query-error");
            SandBoxNavigationCache.Instances[0].DistanceFailure = sentinel;
            Exception found = Failure(delegate { float ratio; proxy.GetSettlementToSettlementDistanceWithLandRatio(null, false, null, true, out ratio); });
            Check(ReferenceEquals(found, sentinel), "original query error replaced or swallowed");
            Check(proxy.MaximumDistanceBetweenTwoConnectedSettlements == value && SandBoxNavigationCache.Instances.Count == 1, "query error regenerated cache");
        }, passed);
        Case("generation failure is preserved and not repeatedly retried", delegate
        {
            CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.Default);
            Exception sentinel = new ApplicationException("sentinel-generation-error");
            SandBoxNavigationCache.DuringGeneration = delegate { throw sentinel; };
            Exception first = Failure(delegate { float unused = proxy.MaximumDistanceBetweenTwoConnectedSettlements; });
            Exception next = Failure(delegate { proxy.GetNeighbors(null); });
            Check(ReferenceEquals(first, sentinel) && ReferenceEquals(next.InnerException, sentinel), "generation failure identity lost");
            Check(SandBoxNavigationCache.Instances.Count == 1 && SandBoxNavigationCache.Instances[0].GenerateCount == 1 && SandBoxNavigationCache.Instances[0].FinalizeCount == 0, "failed cache exposed/retried/finalized");
        }, passed);
        Case("generation and finalization reentry fail without recursive generation", delegate
        {
            foreach (bool finalize in new bool[] { false, true })
            {
                Reset(); CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.All);
                Action<SandBoxNavigationCache> reenter = delegate { proxy.GetNeighbors(null); };
                if (finalize) SandBoxNavigationCache.DuringFinalization = reenter; else SandBoxNavigationCache.DuringGeneration = reenter;
                Exception found = Failure(delegate { float unused = proxy.MaximumDistanceBetweenTwoConnectedSettlements; });
                Check(found is InvalidOperationException && found.Message.Contains("Reentrant"), "reentry did not fail explicitly");
                Check(SandBoxNavigationCache.Instances.Count == 1 && SandBoxNavigationCache.Instances[0].GenerateCount == 1, "reentry recursively generated");
            }
        }, passed);
        Case("missing map scene never generates or returns a dummy distance", delegate
        {
            Campaign.Current = null; CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.Default);
            Exception found = Failure(delegate { float unused = proxy.MaximumDistanceBetweenTwoConnectedSettlements; });
            Check(found is InvalidOperationException && found.Message.Contains("unavailable"), "missing-scene error not explicit");
            Check(SandBoxNavigationCache.Instances.Count == 0, "generated without current scene");
        }, passed);
        Case("simultaneous cold queries produce one complete cache", delegate
        {
            CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.Naval);
            List<Exception> errors = new List<Exception>(); Thread[] threads = new Thread[12];
            SandBoxNavigationCache.DuringGeneration = delegate { Thread.Sleep(20); };
            for (int i = 0; i < threads.Length; i++) threads[i] = new Thread(delegate()
            {
                try { Check(proxy.MaximumDistanceBetweenTwoConnectedSettlements == 1001, "concurrent wrong result"); }
                catch (Exception error) { lock (errors) errors.Add(error); }
            });
            foreach (Thread thread in threads) thread.Start(); foreach (Thread thread in threads) thread.Join();
            Check(errors.Count == 0 && SandBoxNavigationCache.Instances.Count == 1 && SandBoxNavigationCache.Instances[0].GenerateCount == 1 && SandBoxNavigationCache.Instances[0].FinalizeCount == 1, "cold-query race");
        }, passed);
        Case("first-chance recorder is bounded and does not handle failures", delegate
        {
            RuntimeDiagnostics.InstallUnhandledExceptionHook();
            RuntimeDiagnostics.BeginCampaignDiagnostics();
            Campaign.Current = null;
            CurrentMapCache proxy = new CurrentMapCache(MobileParty.NavigationType.All);
            int escaped = 0;
            for (int i = 0; i < 60; i++)
            {
                try { float unused = proxy.MaximumDistanceBetweenTwoConnectedSettlements; }
                catch (InvalidOperationException) { escaped++; }
            }
            Check(escaped == 60, "exception observer handled a game failure");
            string[] recorded = Directory.GetFiles(Path.Combine(logRoot, "Modules", "EastAsiaMap_Trial", "RuntimeDiagnostics"), "*.log");
            string text = File.ReadAllText(recorded[0]);
            int observed = text.Split(new string[] { "managed_exception_observed" }, StringSplitOptions.None).Length - 1;
            Check(observed == 32, "first-chance records were not capped at32");
        }, passed);
        string[] logs = Directory.GetFiles(Path.Combine(logRoot, "Modules", "EastAsiaMap_Trial", "RuntimeDiagnostics"), "*.log");
        Check(logs.Length == 1, "expected process-specific diagnostic log");
        string diagnostic = File.ReadAllText(logs[0]);
        Check(diagnostic.Contains("cache.generate_start") && diagnostic.Contains("cache.finalize_complete") && diagnostic.Contains("cache.generate_failed") && diagnostic.Contains("cache.reentrant_generation") && diagnostic.Contains("sentinel-query-error") && diagnostic.Contains("sentinel-generation-error"), "diagnostics do not retain key success/failure evidence");
        passed.Add("stage and original-exception diagnostics retained");
        return passed.ToArray();
    }
}
