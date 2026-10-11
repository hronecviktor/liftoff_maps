using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiftoffImport
{
    /// <summary>
    /// Puts the map into The Drawing Board: the scan's chunks with colliders
    /// that behave like the level's own floor, the grid floor and its labels
    /// moved below the scan, and the drone's start point raised onto the scan's
    /// surface at the origin.
    /// </summary>
    internal class MapLoader
    {
        // the grid sits a little below the scan's ground so it doesn't show through
        private const float FloorClearance = 0.5f;
        // the drone starts this far above the scan's collision surface
        private const float SpawnClearance = 0.15f;

        private readonly string pluginDir;
        private readonly string configuredFile;
        private MapFile map;
        private bool failed;
        private Material material;
        private bool emissive;

        public MapLoader(string pluginDir, string configuredFile, bool emissive)
        {
            this.pluginDir = pluginDir;
            this.configuredFile = configuredFile;
            this.emissive = emissive;
        }

        /// <summary>
        /// Runs from sceneLoaded, before the level's Start() calls spawn the
        /// drone, so the drone appears on the scan instead of inside it.
        /// Returns the follow-up to run after Start(), or null.
        /// </summary>
        public IEnumerator Install(Scene scene)
        {
            var floor = FindRoot<DrawingBoardFloorCreator>(scene);
            if (floor == null || !EnsureLoaded())
            {
                return null;
            }
            var floorY = map.Floor - FloorClearance;
            // the floor's collider is a separate root object marked as ground
            var ground = FindRoot<GroundObject>(scene);
            if (ground != null)
            {
                ground.transform.position = new Vector3(0, floorY, 0);
            }
            FindRoot<CoordinateSpawner>(scene)?.gameObject.SetActive(false);
            var collisionMaterial = ground != null ? ground.GetComponent<DroneObjectCollider>()?.collisionMaterial : null;

            var root = new GameObject($"LiftoffImport {map.Name}");
            SceneManager.MoveGameObjectToScene(root, scene);
            var colliders = new List<Collider>();
            foreach (var chunk in map.Chunks)
            {
                var visual = new GameObject(chunk.Name);
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = chunk.Render;
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var solid = new GameObject(chunk.Name + " collision");
                solid.transform.SetParent(root.transform, false);
                var meshCollider = solid.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = chunk.Collision;
                colliders.Add(meshCollider);
                solid.AddComponent<GroundObject>();
                solid.AddComponent<DroneObjectCollider>().collisionMaterial = collisionMaterial;
            }

            var start = FindRoot<DroneSpawnPoint>(scene);
            var spawnY = float.NaN;
            if (start != null)
            {
                spawnY = SurfaceBelow(colliders, start.transform.position) + SpawnClearance;
                if (!float.IsNaN(spawnY))
                {
                    var p = start.transform.position;
                    start.transform.position = new Vector3(p.x, spawnY, p.z);
                }
            }
            Plugin.Log.LogInfo($"installed '{map.Name}' in The Drawing Board; grid floor at {floorY:F2} m, " +
                               $"start point at {(start != null ? start.transform.position.ToString("F2") : "(not found)")}, " +
                               $"crash material {(collisionMaterial != null ? collisionMaterial.name : "none")}");
            return AfterStart(scene, floor, floorY, start);
        }

        private IEnumerator AfterStart(Scene scene, DrawingBoardFloorCreator floor, float floorY, DroneSpawnPoint start)
        {
            // the floor builds its tiles in Start(), at world height 0
            yield return null;
            if (!scene.isLoaded)
            {
                yield break;
            }
            floor.transform.position = new Vector3(0, floorY, 0);

            // a drone that already stood at the old start point is inside the scan now
            if (start == null)
            {
                yield break;
            }
            var target = start.transform.position;
            foreach (var root in scene.GetRootGameObjects())
            {
                var body = root.GetComponent<Rigidbody>();
                if (body == null || root.layer != LayerMask.NameToLayer("Drone"))
                {
                    continue;
                }
                var was = body.position;
                var offset = was - target;
                if (new Vector2(offset.x, offset.z).magnitude < 2f && offset.y < 0f)
                {
                    body.position = target;
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    Plugin.Log.LogInfo($"moved drone '{root.name}' from {was:F2} onto the start point");
                }
            }
        }

        private bool EnsureLoaded()
        {
            if (map != null || failed)
            {
                return map != null;
            }
            var mapPath = ResolveMapPath();
            if (mapPath == null)
            {
                failed = true;
                return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                map = MapFile.Load(mapPath);
            }
            catch (System.Exception e)
            {
                failed = true;
                Plugin.Log.LogError($"loading {mapPath}: {e}");
                return false;
            }
            Plugin.Log.LogInfo($"loaded map '{map.Name}' ({map.Chunks.Length} chunks, bounds {map.Bounds}) in {watch.ElapsedMilliseconds} ms");
            return true;
        }

        private string ResolveMapPath()
        {
            if (!string.IsNullOrWhiteSpace(configuredFile))
            {
                var path = Path.Combine(pluginDir, configuredFile.Trim());
                if (File.Exists(path))
                {
                    return path;
                }
                Plugin.Log.LogWarning($"configured map {path} not found; The Drawing Board stays as it is");
                return null;
            }
            var maps = Path.Combine(pluginDir, "maps");
            var found = Directory.Exists(maps) ? Directory.GetFiles(maps, "*.lmap", SearchOption.AllDirectories) : new string[0];
            System.Array.Sort(found, System.StringComparer.Ordinal);
            if (found.Length == 0)
            {
                Plugin.Log.LogWarning($"no .lmap file under {maps}; The Drawing Board stays as it is");
                return null;
            }
            if (found.Length > 1)
            {
                Plugin.Log.LogInfo($"{found.Length} maps found, using the first; set [Map] File to pick another: {string.Join(", ", found)}");
            }
            return found[0];
        }

        /// <summary>Highest point of the scan's collision surface below (and around) a point.</summary>
        private static float SurfaceBelow(List<Collider> colliders, Vector3 point)
        {
            Physics.SyncTransforms(); // new colliders are not raycastable before the next physics step
            var from = new Vector3(point.x, 1000f, point.z);
            var best = float.NaN;
            foreach (var c in colliders)
            {
                if (c.Raycast(new Ray(from, Vector3.down), out var hit, 2000f) && !(hit.point.y <= best))
                {
                    best = hit.point.y;
                }
            }
            return best;
        }

        /// <summary>Switches between lit (default) and emissive shading of the scan.</summary>
        public void ToggleShading()
        {
            emissive = !emissive;
            if (material != null)
            {
                Configure(material);
            }
            Plugin.Log.LogInfo($"map shading: {(emissive ? "emissive (baked lighting only)" : "lit")}");
        }

        private Material Material
        {
            get
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse"))
                    {
                        name = $"LiftoffImport {map.Name}",
                        hideFlags = HideFlags.DontUnloadUnusedAsset,
                    };
                    Configure(material);
                }
                return material;
            }
        }

        // Photogrammetry textures already contain the scene's lighting. Lit
        // shading adds the level's sun on top (darker, double shadows) but works
        // with any shader variant; emissive shows the texture as captured.
        private void Configure(Material m)
        {
            m.mainTexture = map.Texture;
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_Metallic", 0f);
            if (emissive)
            {
                m.color = Color.black;
                m.SetTexture("_EmissionMap", map.Texture);
                m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.color = Color.white;
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
        }

        private static T FindRoot<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponent<T>();
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
