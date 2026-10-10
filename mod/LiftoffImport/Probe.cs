using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiftoffImport
{
    /// <summary>
    /// Read-only diagnostics: what the game's scenes are made of, which shaders
    /// exist and where the drone is. Nothing in the game is changed.
    /// </summary>
    internal class Probe
    {
        // Built-in shaders a runtime-loaded mesh could use; only those the
        // build includes can be found.
        private static readonly string[] ShaderCandidates =
        {
            "Unlit/Texture", "Unlit/Color", "Standard", "Standard (Specular setup)",
            "Legacy Shaders/Diffuse", "Mobile/Diffuse", "Mobile/Unlit (Supports Lightmap)",
            "Nature/Terrain/Standard", "Sprites/Default", "UI/Default", "Hidden/InternalErrorShader",
        };

        private readonly string outDir;

        public Probe(string bepInExRoot)
        {
            outDir = Path.Combine(bepInExRoot, "LiftoffImport");
            Directory.CreateDirectory(outDir);
        }

        public void LogEnvironment()
        {
            foreach (var name in ShaderCandidates)
            {
                var shader = Shader.Find(name);
                Plugin.Log.LogInfo($"shader {(shader != null && shader.isSupported ? "found  " : "missing")} {name}");
            }
            var layers = Enumerable.Range(0, 32)
                .Select(i => (i, name: LayerMask.LayerToName(i)))
                .Where(l => l.name.Length > 0)
                .Select(l => $"{l.i}={l.name}");
            Plugin.Log.LogInfo("layers " + string.Join(", ", layers));
        }

        public IEnumerator OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Plugin.Log.LogInfo($"scene loaded: '{scene.name}' ({mode}, build index {scene.buildIndex}, path {scene.path})");
            // let the level finish spawning the drone and track
            yield return new WaitForSeconds(3f);
            if (!scene.isLoaded)
            {
                yield break;
            }
            Plugin.Log.LogInfo(Summarize(scene));
        }

        private static string Summarize(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"scene '{scene.name}': {roots.Length} roots, {all.Count} objects");

            var antiCheat = all.SelectMany(g => g.GetComponents<Component>())
                .Where(c => c != null && (c.GetType().Namespace ?? "").StartsWith("CodeStage.AntiCheat"))
                .Select(c => $"{c.GetType().Name} on '{c.gameObject.name}'").ToList();
            sb.AppendLine($"  anti-cheat components: {(antiCheat.Count == 0 ? "none" : string.Join("; ", antiCheat))}");

            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToList();
            var shaders = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != null)
                .GroupBy(m => m.shader.name).OrderByDescending(g => g.Count()).Take(15)
                .Select(g => $"{g.Key} x{g.Count()}");
            sb.AppendLine($"  renderers: {renderers.Count}; shaders: {string.Join(", ", shaders)}");

            var colliders = roots.SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToList();
            var colliderLayers = colliders.GroupBy(c => LayerMask.LayerToName(c.gameObject.layer))
                .Select(g => $"{g.Key} x{g.Count()}");
            sb.AppendLine($"  colliders: {colliders.Count} by layer: {string.Join(", ", colliderLayers)}");

            foreach (var t in roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)))
            {
                sb.AppendLine($"  terrain '{t.name}' at {t.transform.position} size {t.terrainData?.size}");
            }
            foreach (var rb in roots.SelectMany(r => r.GetComponentsInChildren<Rigidbody>(true)).Take(20))
            {
                sb.AppendLine($"  rigidbody '{PathOf(rb.transform)}' mass {rb.mass} at {rb.position} layer {LayerMask.LayerToName(rb.gameObject.layer)}");
            }
            foreach (var cam in roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)))
            {
                sb.AppendLine($"  camera '{PathOf(cam.transform)}' enabled {cam.enabled} at {cam.transform.position} far {cam.farClipPlane}");
            }
            foreach (var root in roots)
            {
                sb.AppendLine($"  root '{root.name}' active {root.activeSelf} children {root.transform.childCount}");
            }
            return sb.ToString();
        }

        /// <summary>Writes every loaded scene's hierarchy (with components) to a file.</summary>
        public void Dump()
        {
            var file = Path.Combine(outDir, $"dump-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            using (var w = new StreamWriter(file))
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded)
                    {
                        continue;
                    }
                    w.WriteLine(Summarize(scene));
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        Write(w, root.transform, 0);
                    }
                    w.WriteLine();
                }
            }
            Plugin.Log.LogInfo($"wrote {file}");
        }

        private static void Write(StreamWriter w, Transform t, int depth)
        {
            var components = t.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name);
            w.WriteLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")} " +
                        $"[{LayerMask.LayerToName(t.gameObject.layer)}] pos {t.position} {{{string.Join(", ", components)}}}");
            // large levels have thousands of props; their structure is what matters
            if (depth >= 5)
            {
                if (t.childCount > 0)
                {
                    w.WriteLine($"{new string(' ', (depth + 1) * 2)}… {t.childCount} children");
                }
                return;
            }
            foreach (Transform child in t)
            {
                Write(w, child, depth + 1);
            }
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent)
            {
                parts.Add(t.name);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
