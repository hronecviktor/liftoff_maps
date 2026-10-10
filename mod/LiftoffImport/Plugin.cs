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
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private ConfigEntry<KeyboardShortcut> dumpKey;
        private Probe probe;

        private void Awake()
        {
            Log = Logger;
            dumpKey = Config.Bind("Probe", "DumpKey", new KeyboardShortcut(KeyCode.F8),
                "Write a hierarchy dump of the loaded scenes to BepInEx/LiftoffImport/");
            probe = new Probe(Paths.BepInExRootPath);

            Log.LogInfo($"{Name} {Version} loaded: Unity {Application.unityVersion}, game {Application.version}");
            probe.LogEnvironment();
            SceneManager.sceneLoaded += (scene, mode) => StartCoroutine(probe.OnSceneLoaded(scene, mode));
        }

        private void Update()
        {
            if (dumpKey.Value.IsDown())
            {
                probe.Dump();
            }
        }
    }
}
