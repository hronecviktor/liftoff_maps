using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiftoffImport
{
    /// <summary>
    /// Puts the map into The Drawing Board: the grid floor and its labels move
    /// below the scan, the scan's chunks are added with colliders that behave
    /// like the level's own floor. The drone spawns at the origin, which the
    /// converter places on the scan's surface.
    /// </summary>
    internal class MapLoader
    {
        // the grid sits a little below the scan's ground so it doesn't show through
        private const float FloorClearance = 0.5f;

        private readonly string mapPath;
        private MapFile map;
        private bool failed;
        private Material material;
        private bool emissive;

        public MapLoader(string mapPath, bool emissive)
        {
            this.mapPath = mapPath;
            this.emissive = emissive;
        }

        public IEnumerator OnSceneLoaded(Scene scene)
        {
            // wait for the level's own Start() calls (the floor builds its tiles there)
            yield return null;
            yield return new WaitForEndOfFrame();
            var floor = scene.isLoaded ? FindRoot<DrawingBoardFloorCreator>(scene) : null;
            if (floor == null)
            {
                yield break;
            }
            if (map == null && !failed)
            {
                if (!File.Exists(mapPath))
                {
                    Plugin.Log.LogWarning($"no map at {mapPath}; The Drawing Board stays as it is");
                    failed = true;
                    yield break;
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
                    yield break;
                }
                Plugin.Log.LogInfo($"loaded map '{map.Name}' ({map.Chunks.Length} chunks, bounds {map.Bounds}) in {watch.ElapsedMilliseconds} ms");
            }
            if (map == null)
            {
                yield break;
            }
            Install(scene, floor);
        }

        private void Install(Scene scene, DrawingBoardFloorCreator floor)
        {
            var floorY = map.Floor - FloorClearance;
            floor.transform.position = new Vector3(0, floorY, 0);
            // the floor's collider is a separate object marked as ground
            var ground = FindRoot<GroundObject>(scene);
            if (ground != null)
            {
                ground.transform.position = new Vector3(0, floorY, 0);
            }
            FindRoot<CoordinateSpawner>(scene)?.gameObject.SetActive(false);
            var collisionMaterial = ground != null ? ground.GetComponent<DroneObjectCollider>()?.collisionMaterial : null;

            var root = new GameObject($"LiftoffImport {map.Name}");
            SceneManager.MoveGameObjectToScene(root, scene);
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
                solid.AddComponent<MeshCollider>().sharedMesh = chunk.Collision;
                solid.AddComponent<GroundObject>();
                var hit = solid.AddComponent<DroneObjectCollider>();
                hit.collisionMaterial = collisionMaterial;
            }
            Plugin.Log.LogInfo($"installed '{map.Name}' in The Drawing Board; grid floor at {floorY:F2} m, " +
                               $"crash material {(collisionMaterial != null ? collisionMaterial.name : "none")}");
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
