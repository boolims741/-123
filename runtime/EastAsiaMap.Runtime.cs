// Bannerlord 1.5.6.125053 candidate. Logs show stock-map caches loading for
// the replacement map; rebuilding via public APIs does not prove crash cause.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using NavalDLC.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Map.DistanceCache;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace EastAsiaMap.Runtime
{
    public sealed class EastAsiaMapSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            RuntimeDiagnostics.InstallUnhandledExceptionHook();
            RuntimeDiagnostics.Write("module.loaded version=v0.10.0.0 target=1.5.6.125053");
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (game == null || !(game.GameType is Campaign)) return;
            RuntimeDiagnostics.BeginCampaignDiagnostics();
            try
            {
                CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
                if (starter == null)
                    throw new InvalidOperationException("Campaign started without CampaignGameStarter.");
                starter.AddModel((GameModel)new EastAsiaNavalMapDistanceModel());
                RuntimeDiagnostics.Write("campaign.model_registered EastAsiaNavalMapDistanceModel");
            }
            catch (Exception error)
            {
                RuntimeDiagnostics.Error("campaign.model_registration_failed", error);
                throw;
            }
        }
    }

    public sealed class EastAsiaNavalMapDistanceModel : NavalDLCMapDistanceModel
    {
        public override void RegisterDistanceCache(MobileParty.NavigationType navigationType,
                                                   MapDistanceModel.INavigationCache stockCache)
        {
            // Keep the real NavalDLC distance operations and replace only their
            // cached map data. Never use Euclidean-distance substitutes.
            RuntimeDiagnostics.Write("cache.register capability=" + navigationType);
            try
            {
                base.RegisterDistanceCache(navigationType, new CurrentMapCache(navigationType));
            }
            catch (Exception error)
            {
                RuntimeDiagnostics.Error("cache.register_failed capability=" + navigationType, error);
                throw;
            }
        }
    }

    internal sealed class CurrentMapCache : MapDistanceModel.INavigationCache
    {
        private readonly MobileParty.NavigationType _navigationType;
        private readonly object _gate = new object();
        private readonly HashSet<string> _observedQueries = new HashSet<string>();
        private SandBoxNavigationCache _cache;
        private Exception _generationFailure;
        private bool _generating;

        internal CurrentMapCache(MobileParty.NavigationType navigationType)
        {
            _navigationType = navigationType;
        }

        private SandBoxNavigationCache GetCache(string query)
        {
            lock (_gate)
            {
                if (_observedQueries.Add(query))
                    RuntimeDiagnostics.Write("cache.query capability=" + _navigationType + " member=" + query);
                if (_cache != null) return _cache;
                if (_generationFailure != null)
                    throw new InvalidOperationException("Current-map cache generation previously failed for " +
                                                        _navigationType + ". See RuntimeDiagnostics.", _generationFailure);
                if (_generating)
                {
                    InvalidOperationException error = new InvalidOperationException(
                        "Reentrant current-map cache generation for " + _navigationType + " during " + query + ".");
                    RuntimeDiagnostics.Error("cache.reentrant_generation", error);
                    throw error;
                }
                _generating = true;
                try
                {
                    if (Campaign.Current == null || Campaign.Current.MapSceneWrapper == null)
                        throw new InvalidOperationException("Campaign map scene is unavailable during " + query + ".");
                    RuntimeDiagnostics.Write("cache.generate_start capability=" + _navigationType);
                    SandBoxNavigationCache generated = new SandBoxNavigationCache(_navigationType);
                    generated.GenerateCacheData();
                    RuntimeDiagnostics.Write("cache.generate_complete capability=" + _navigationType);
                    generated.FinalizeInitialization();
                    RuntimeDiagnostics.Write("cache.finalize_complete capability=" + _navigationType);
                    _cache = generated;
                    return generated;
                }
                catch (Exception error)
                {
                    _generationFailure = error;
                    RuntimeDiagnostics.Error("cache.generate_failed capability=" + _navigationType, error);
                    throw;
                }
                finally { _generating = false; }
            }
        }

        public float MaximumDistanceBetweenTwoConnectedSettlements
        {
            get
            {
                try { return GetCache("MaximumDistance").MaximumDistanceBetweenTwoConnectedSettlements; }
                catch (Exception error) { RuntimeDiagnostics.Error("cache.query_failed capability=" + _navigationType + " member=MaximumDistance", error); throw; }
            }
        }

        public float GetSettlementToSettlementDistanceWithLandRatio(Settlement settlement1, bool isPort1,
                                                                    Settlement settlement2, bool isPort2,
                                                                    out float landRatio)
        {
            try
            {
                MapDistanceModel.INavigationCache cache = GetCache("SettlementDistance");
                return cache.GetSettlementToSettlementDistanceWithLandRatio(settlement1, isPort1,
                                                                             settlement2, isPort2, out landRatio);
            }
            catch (Exception error) { RuntimeDiagnostics.Error("cache.query_failed capability=" + _navigationType + " member=SettlementDistance", error); throw; }
        }

        public MBReadOnlyList<Settlement> GetNeighbors(Settlement settlement)
        {
            try { return GetCache("Neighbors").GetNeighbors(settlement); }
            catch (Exception error) { RuntimeDiagnostics.Error("cache.query_failed capability=" + _navigationType + " member=Neighbors", error); throw; }
        }

        public Settlement GetClosestSettlementToFaceIndex(int faceIndex, out bool isPortUsed)
        {
            try { return GetCache("ClosestSettlement").GetClosestSettlementToFaceIndex(faceIndex, out isPortUsed); }
            catch (Exception error)
            {
                RuntimeDiagnostics.Error("cache.query_failed capability=" + _navigationType +
                                         " member=ClosestSettlement face=" + faceIndex.ToString(CultureInfo.InvariantCulture), error);
                throw;
            }
        }

        public void FinalizeInitialization()
        {
            // Scene OnInit may register before Campaign.MapSceneWrapper is set.
            // The generated cache is finalized at its first actual query.
            RuntimeDiagnostics.Write("cache.finalization_deferred capability=" + _navigationType);
        }
    }

    internal static class RuntimeDiagnostics
    {
        private static readonly object Gate = new object();
        private static int _hookInstalled;
        private static int _remainingRelevantExceptions;
        [ThreadStatic] private static bool _insideExceptionRecorder;
        private static string _filePath;

        internal static void InstallUnhandledExceptionHook()
        {
            if (Interlocked.Exchange(ref _hookInstalled, 1) != 0) return;
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
            {
                Exception exception = args.ExceptionObject as Exception;
                if (exception != null) Error("unhandled_exception terminating=" + args.IsTerminating, exception);
                else Write("unhandled_exception terminating=" + args.IsTerminating + " object=" + args.ExceptionObject);
            };
            AppDomain.CurrentDomain.FirstChanceException += RecordRelevantException;
        }

        internal static void BeginCampaignDiagnostics()
        {
            // Framework crash handlers can intercept an exception before the
            // UnhandledException event. Capture a bounded set of relevant
            // managed failures at throw time without handling those failures.
            Interlocked.Exchange(ref _remainingRelevantExceptions, 32);
            Write("campaign.diagnostics_enabled relevant_exception_limit=32");
        }

        private static void RecordRelevantException(object sender, FirstChanceExceptionEventArgs args)
        {
            if (_insideExceptionRecorder || Volatile.Read(ref _remainingRelevantExceptions) <= 0) return;
            Exception error = args.Exception;
            if (!(error is NullReferenceException || error is IndexOutOfRangeException ||
                  error is KeyNotFoundException || error is ArgumentOutOfRangeException ||
                  error is InvalidOperationException)) return;
            _insideExceptionRecorder = true;
            try
            {
                string stack = error.StackTrace ?? String.Empty;
                if (stack.IndexOf("TaleWorlds.CampaignSystem", StringComparison.Ordinal) < 0 &&
                    stack.IndexOf("NavalDLC", StringComparison.Ordinal) < 0 &&
                    stack.IndexOf("SandBox.", StringComparison.Ordinal) < 0 &&
                    stack.IndexOf("EastAsiaMap.Runtime", StringComparison.Ordinal) < 0) return;
                if (Interlocked.Decrement(ref _remainingRelevantExceptions) >= 0)
                    Error("managed_exception_observed (may be handled by game)", error);
            }
            catch (Exception recorderError)
            {
                Trace.WriteLine("EastAsiaMap exception recorder failed: " + recorderError.GetType().Name);
            }
            finally { _insideExceptionRecorder = false; }
        }

        internal static void Error(string stage, Exception error)
        {
            Write(stage + Environment.NewLine + error.ToString());
        }

        internal static void Write(string message)
        {
            // Diagnostic IO must never suppress or replace a game exception.
            // No process environment, credentials, game-state or memory dump.
            lock (Gate)
            {
                string line = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) +
                              " thread=" + Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture) +
                              " " + message + Environment.NewLine;
                try
                {
                    if (_filePath == null) _filePath = CreateLogPath(false);
                    File.AppendAllText(_filePath, line, new UTF8Encoding(false));
                }
                catch (Exception firstWriteError)
                {
                    try
                    {
                        _filePath = CreateLogPath(true);
                        File.AppendAllText(_filePath, line, new UTF8Encoding(false));
                    }
                    catch (Exception fallbackWriteError)
                    {
                        Trace.WriteLine("EastAsiaMap RuntimeDiagnostics IO failed: " +
                                        firstWriteError.GetType().Name + "; " + fallbackWriteError.GetType().Name);
                    }
                }
            }
        }

        private static string CreateLogPath(bool useLocalAppData)
        {
            string directory = useLocalAppData
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "EastAsiaMap_Trial", "RuntimeDiagnostics")
                : Path.Combine(BasePath.Name, "Modules", "EastAsiaMap_Trial", "RuntimeDiagnostics");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "eastasia-runtime-" +
                                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + ".log");
        }
    }
}
