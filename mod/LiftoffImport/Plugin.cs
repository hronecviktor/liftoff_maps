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
        public const string Version = "0.1.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static Probe Probe;

        private void Awake()
        {
            Log = Logger;
            DumpKey = Config.Bind("Probe", "DumpKey", new KeyboardShortcut(KeyCode.F8),
                "Write a hierarchy dump of the loaded scenes to BepInEx/LiftoffImport/");
            Probe = new Probe(Paths.BepInExRootPath);

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
            Guard("scene loaded", () => Runner.Ensure().StartCoroutine(Runner.Safe("scene probe", Probe.OnSceneLoaded(scene, mode))));
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
