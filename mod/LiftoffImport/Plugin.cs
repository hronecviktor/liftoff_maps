using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiftoffImport
{
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "is.0s.liftoff-import";
        public const string Name = "Liftoff Import";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static ConfigEntry<KeyboardShortcut> ShadingKey;
        internal static Probe Probe;
        internal static MapLoader Map;

        private void Awake()
        {
            Log = Logger;
            DumpKey = Config.Bind("Probe", "DumpKey", new KeyboardShortcut(KeyCode.F8),
                "Write a hierarchy dump of the loaded scenes to BepInEx/LiftoffImport/");
            Probe = new Probe(Paths.BepInExRootPath);
            var mapFile = Config.Bind("Map", "File", "maps/pirita/pirita_full.lmap",
                "Map loaded into The Drawing Board, relative to this plugin's folder (or absolute)");
            var emissive = Config.Bind("Map", "Emissive", false,
                "Show the scan's texture as captured instead of lit by the level's sun");
            ShadingKey = Config.Bind("Map", "ShadingKey", new KeyboardShortcut(KeyCode.F9),
                "Switch between lit and emissive shading");
            Map = new MapLoader(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Info.Location), mapFile.Value), emissive.Value);

            Log.LogInfo($"{Name} {Version} loaded: Unity {Application.unityVersion}, game {Application.version}");
            Guard("environment", Probe.LogEnvironment);
            SceneManager.sceneLoaded += OnSceneLoaded;
            Runner.Ensure();
        }

        // Liftoff destroys BepInEx's manager object (and this component with it)
        // during startup, so nothing here may depend on this MonoBehaviour.
        // Exceptions must not escape either: one throwing sceneLoaded handler stops
        // the game's own handlers, and the game uploads unhandled ones as crash reports.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Guard("scene loaded", () =>
            {
                var runner = Runner.Ensure();
                runner.StartCoroutine(Runner.Safe("scene probe", Probe.OnSceneLoaded(scene, mode)));
                runner.StartCoroutine(Runner.Safe("map", Map.OnSceneLoaded(scene)));
            });
        }

        internal static void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.LogError($"{what}: {e}");
            }
        }
    }

    /// <summary>Hidden object that survives scene changes and runs coroutines and input.</summary>
    internal class Runner : MonoBehaviour
    {
        private static Runner instance;

        public static Runner Ensure()
        {
            if (instance == null)
            {
                var go = new GameObject("LiftoffImport") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Runner>();
                Plugin.Log.LogInfo("runner created");
            }
            return instance;
        }

        private void Update()
        {
            if (Plugin.DumpKey.Value.IsDown())
            {
                Plugin.Guard("dump", Plugin.Probe.Dump);
            }
            if (Plugin.ShadingKey.Value.IsDown())
            {
                Plugin.Guard("shading", Plugin.Map.ToggleShading);
            }
        }

        // a failing step in a coroutine must not surface as an unhandled exception
        public static IEnumerator Safe(string what, IEnumerator routine)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext())
                    {
                        yield break;
                    }
                    current = routine.Current;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"{what}: {e}");
                    yield break;
                }
                yield return current;
            }
        }
    }
}
