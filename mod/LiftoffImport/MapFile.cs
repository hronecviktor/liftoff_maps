using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiftoffImport
{
    /// <summary>
    /// A map written by tools/convert_scan.py: chunked render and collision
    /// meshes in Unity coordinates, plus a texture. Loaded once and kept across
    /// level loads.
    /// </summary>
    internal class MapFile
    {
        public string Name;
        public float Floor;
        public Bounds Bounds;
        public Texture2D Texture;
        public Chunk[] Chunks;

        public class Chunk
        {
            public string Name;
            public Mesh Render;
            public Mesh Collision;
        }

        public static MapFile Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "LMAP")
            {
                throw new InvalidDataException($"{path} is not an .lmap file");
            }
            var version = BitConverter.ToInt32(bytes, 4);
            if (version != 1)
            {
                throw new InvalidDataException($"{path}: unsupported .lmap version {version}");
            }
            var headLength = BitConverter.ToInt32(bytes, 8);
            var head = JObject.Parse(Encoding.UTF8.GetString(bytes, 12, headLength));
            var data = 12 + headLength;
            data += (4 - data % 4) % 4;

            var map = new MapFile
            {
                Name = (string)head["name"],
                Floor = (float)head["floor"],
                Bounds = ToBounds(head["bounds"]),
            };
            var chunks = (JArray)head["chunks"];
            map.Chunks = new Chunk[chunks.Count];
            for (var i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                var name = (string)c["name"];
                map.Chunks[i] = new Chunk
                {
                    Name = name,
                    Render = ReadMesh(bytes, data, c["render"], $"{map.Name} {name}", withUvs: true),
                    Collision = ReadMesh(bytes, data, c["collision"], $"{map.Name} {name} collision", withUvs: false),
                };
            }

            var texturePath = Path.Combine(Path.GetDirectoryName(path), (string)head["texture"]);
            map.Texture = LoadTexture(texturePath);
            return map;
        }

        private static Mesh ReadMesh(byte[] bytes, int data, JToken spec, string name, bool withUvs)
        {
            var vertexCount = (int)spec["vertices"];
            var triangleCount = (int)spec["triangles"];

            var floats = new float[vertexCount * 3];
            Buffer.BlockCopy(bytes, data + (int)spec["positions"], floats, 0, floats.Length * 4);
            var positions = new Vector3[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                positions[i] = new Vector3(floats[i * 3], floats[i * 3 + 1], floats[i * 3 + 2]);
            }

            var indices = new int[triangleCount * 3];
            Buffer.BlockCopy(bytes, data + (int)spec["indices"], indices, 0, indices.Length * 4);

            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                // the game unloads unused assets on every scene change
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            mesh.vertices = positions;
            if (withUvs)
            {
                var uvFloats = new float[vertexCount * 2];
                Buffer.BlockCopy(bytes, data + (int)spec["uvs"], uvFloats, 0, uvFloats.Length * 4);
                var uvs = new Vector2[vertexCount];
                for (var i = 0; i < vertexCount; i++)
                {
                    uvs[i] = new Vector2(uvFloats[i * 2], uvFloats[i * 2 + 1]);
                }
                mesh.uv = uvs;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            if (withUvs)
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: withUvs); // collision meshes stay readable for PhysX
            return mesh;
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: true)
            {
                name = Path.GetFileName(path),
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 8,
            };
            if (!texture.LoadImage(File.ReadAllBytes(path), markNonReadable: false))
            {
                throw new InvalidDataException($"could not decode {path}");
            }
            texture.Compress(highQuality: true);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        private static Bounds ToBounds(JToken t)
        {
            var min = new Vector3((float)t[0][0], (float)t[0][1], (float)t[0][2]);
            var max = new Vector3((float)t[1][0], (float)t[1][1], (float)t[1][2]);
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }
    }
}
