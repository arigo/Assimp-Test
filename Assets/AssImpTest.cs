using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Assimp.Unmanaged;


public class AssImpTest : MonoBehaviour
{
    public Material material;
    /*public*/ Mesh[] meshes;
    /*public*/ Material[] materials;
    Material[] mesh2mat;


    private void Start()
    {
        //string filename = "D:\\Temp\\Cliff house dynamic v2.dae";
        //string filename = "D:\\Temp\\Untitled 2.dae";
        //string filename = "D:\\Temp\\Untitled.fbx";
        string filename = "D:\\Temp\\ogre.mdl";
        ImportFile(filename, (ptr) => Display(filename, ptr));
    }

    void Display(string filename, IntPtr ptr)
    {
        AiScene scene = Assimp.MemoryHelper.Read<AiScene>(ptr);
        Debug.Log(scene.NumMeshes + " meshes");
        Debug.Log(scene.NumMaterials + " materials");
        Debug.Log(scene.NumTextures + " internal textures");

        var texloader = new TextureLoader { scene = scene, basefilename = filename };
        materials = new Material[scene.NumMaterials];
        int materials_i = 0;
        foreach (var mat in Assimp.MemoryHelper.FromNativeArray<Assimp.Material, AiMaterial>(scene.Materials, (int)scene.NumMaterials, true))
        {
            var mat1 = new Material(material);
            mat1.color = ToColor(mat.ColorDiffuse);

            if (mat.HasTextureDiffuse)
            {
                var tex = texloader.Load(mat);
                if (tex != null)
                    mat1.mainTexture = tex;
            }

            materials[materials_i++] = mat1;
        }

        mesh2mat = new Material[scene.NumMeshes];
        meshes = new Mesh[scene.NumMeshes];
        int meshes_i = 0;
        foreach (var mesh in ReadArrayOfPtr<AiMesh>(scene.Meshes, scene.NumMeshes))
        {
            //Debug.Log("   - " + mesh.Name);

            var mesh1 = new Mesh();
            mesh1.name = mesh.Name.GetString();

            var vertices = ReadArrayVector3(mesh.Vertices, mesh.NumVertices);
            mesh1.vertices = vertices;

            if (mesh.TextureCoords.Length > 0)
            {
                IntPtr texCoordsPtr = mesh.TextureCoords[0];
                if (texCoordsPtr != IntPtr.Zero)
                    mesh1.uv = ReadArrayVector3_to_Vector2(texCoordsPtr, mesh.NumVertices);
            }

            List<int> tris = new List<int>();
            AiFace[] faces = ReadArrayInline<AiFace>(mesh.Faces, mesh.NumFaces);
            foreach (var face in faces)
            {
                var triangle = ReadArrayInt(face.Indices, face.NumIndices);
                if (triangle.Length == 3)
                {
                    /* XXX for Unity to display meshes properly if they are used in a node with
                     * a negative-determinant transformation, we'd need to swap the orientation
                     * here. */
                    tris.Add(triangle[0]);
                    tris.Add(triangle[2]);
                    tris.Add(triangle[1]);
                }
                else
                {
                    string s = string.Join(", ", triangle.Select(i => vertices[i]));
                    Debug.Log("polygon with " + triangle.Length + " vertices: " + s);
                }
            }
            mesh1.SetTriangles(tris, 0);
            mesh1.RecalculateBounds();
            mesh1.RecalculateNormals();

            mesh2mat[meshes_i] = materials[mesh.MaterialIndex];
            meshes[meshes_i++] = mesh1;
        }

        AiNode root = Assimp.MemoryHelper.Read<AiNode>(scene.RootNode);
        Recurse(transform, root);
    }

    class TextureLoader
    {
        internal AiScene scene;
        internal string basefilename;

        Dictionary<string, Texture2D> textures;
        AiTexture[] scene_textures;

        internal Texture2D Load(Assimp.Material mat)
        {
            var tex = mat.TextureDiffuse;
            if (string.IsNullOrEmpty(tex.FilePath))
                return null;
            if (Path.IsPathRooted(tex.FilePath))
            {
                Debug.LogError("Skipping loading texture from: " + tex.FilePath);
                return null;
            }

            Texture2D tex1;
            if (textures == null)
                textures = new Dictionary<string, Texture2D>();
            if (textures.TryGetValue(tex.FilePath, out tex1))
                return tex1;

            try
            {
                int tex_index;
                if (tex.FilePath.StartsWith("*") && tex.FilePath.Length > 1 && int.TryParse(tex.FilePath.Substring(1), out tex_index))
                    tex1 = BuildTextureFromInternal(tex_index);
                else
                    tex1 = BuildTextureFromExternal(tex.FilePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                tex1 = null;
            }
            textures[tex.FilePath] = tex1;
            return tex1;
        }

        Texture2D BuildTextureFromExternal(string filename)
        {
            filename = Path.Combine(Path.GetDirectoryName(basefilename), filename);
            byte[] all_bytes = File.ReadAllBytes(filename);
            var tex1 = new Texture2D(1, 1);
            tex1.LoadImage(all_bytes, markNonReadable: true);
            return tex1;
        }

        Texture2D BuildTextureFromInternal(int tex_index)
        {
            if (scene_textures == null)
                scene_textures = ReadArrayOfPtr<AiTexture>(scene.Textures, scene.NumTextures);
            var tex = scene_textures[tex_index];
            if (tex.Height != 0)
            {
                var tex1 = new Texture2D((int)tex.Width, (int)tex.Height, TextureFormat.RGBA32, true);
                tex1.SetPixels32(ReadArrayColor32BottomUp(tex.Data, (int)tex.Width, (int)tex.Height));
                tex1.Apply(true, true);
                return tex1;
            }
            else
            {
                var tex1 = new Texture2D(1, 1);
                tex1.LoadImage(ReadArrayBytes(tex.Data, tex.Width), markNonReadable: true);
                return tex1;
            }
        }
    }

    public static Vector3 ToVector3(Assimp.Vector3D v)
    {
        return new Vector3(v.X, v.Y, v.Z);
    }

    public static Quaternion ToQuaternion(Assimp.Quaternion q)
    {
        return new Quaternion(q.X, q.Y, q.Z, q.W);
    }

    public static Color ToColor(Assimp.Color4D c)
    {
        return new Color(c.R, c.G, c.B, c.A);
    }

    public static T[] ReadArrayOfPtr<T>(IntPtr array_of_pointers, uint count) where T : struct
    {
        T[] result = new T[count];
        for (int i = 0; i < count; i++)
        {
            IntPtr currPos = System.Runtime.InteropServices.Marshal.ReadIntPtr(array_of_pointers, IntPtr.Size * i);
            result[i] = Assimp.MemoryHelper.Read<T>(currPos);
        }
        return result;
    }

    public static T[] ReadArrayInline<T>(IntPtr array_inline, uint count) where T : struct
    {
        T[] result = new T[count];
        int size = Assimp.InternalInterop.SizeOfInline<T>();
        for (int i = 0; i < count; i++)
        {
            IntPtr currPos = Assimp.MemoryHelper.AddIntPtr(array_inline, size * i);
            result[i] = Assimp.MemoryHelper.Read<T>(currPos);
        }
        return result;
    }

    public static Vector3[] ReadArrayVector3(IntPtr array_inline, uint count)
    {
        Vector3[] result = new Vector3[count];
        int size = Assimp.InternalInterop.SizeOfInline<Assimp.Vector3D>();
        for (int i = 0; i < count; i++)
        {
            IntPtr currPos = Assimp.MemoryHelper.AddIntPtr(array_inline, size * i);
            Assimp.Vector3D v = Assimp.MemoryHelper.Read<Assimp.Vector3D>(currPos);
            result[i] = ToVector3(v);
        }
        return result;
    }

    public static Vector2[] ReadArrayVector3_to_Vector2(IntPtr array_inline, uint count)
    {
        Vector2[] result = new Vector2[count];
        int size = Assimp.InternalInterop.SizeOfInline<Assimp.Vector3D>();
        for (int i = 0; i < count; i++)
        {
            IntPtr currPos = Assimp.MemoryHelper.AddIntPtr(array_inline, size * i);
            Assimp.Vector3D v = Assimp.MemoryHelper.Read<Assimp.Vector3D>(currPos);
            result[i] = new Vector2(v.X, v.Y);
        }
        return result;
    }

    public static int[] ReadArrayInt(IntPtr array_inline, uint count)
    {
        int[] result = new int[count];
        System.Runtime.InteropServices.Marshal.Copy(array_inline, result, 0, (int)count);
        return result;
    }

    public static byte[] ReadArrayBytes(IntPtr array_inline, uint nbytes)
    {
        byte[] result = new byte[nbytes];
        System.Runtime.InteropServices.Marshal.Copy(array_inline, result, 0, (int)nbytes);
        return result;
    }

    public static Color32[] ReadArrayColor32BottomUp(IntPtr array_inline, int width, int height)
    {
        Color32[] result = new Color32[width * height];
        for (int j = 0; j < height; j++)
        {
            int lineofs1 = (height - 1 - j) * width;
            int lineofs2 = j * width;
            for (int i = 0; i < width; i++)
            {
                uint value = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(array_inline, 4 * (lineofs1 + i));
                result[lineofs2 + i] = new Color32(
                    (byte)(value >> 16),
                    (byte)(value >> 8),
                    (byte)(value >> 0),
                    (byte)(value >> 24));
            }
        }
        return result;
    }

    void Recurse(Transform parent, AiNode node)
    {
        //Debug.Log("node name: " + node.Name);
        //Debug.Log("numchildren: " + node.NumChildren);

        Transform tr;
        if (node.Transformation.IsIdentity && node.NumMeshes == 0 && node.NumChildren == 1)
        {
            tr = parent;   /* skip this intermediate level */
        }
        else
        {
            tr = new GameObject(node.Name.GetString()).transform;
            tr.SetParent(parent);

            Assimp.Vector3D scale, translation;
            Assimp.Quaternion rotation;
            node.Transformation.Decompose(out scale, out rotation, out translation);
            tr.localPosition = ToVector3(translation);
            tr.localRotation = ToQuaternion(rotation);
            tr.localScale = ToVector3(scale);

            int mesh_i = 0;
            foreach (var mesh_index in ReadArrayInt(node.Meshes, node.NumMeshes))
            {
                var tr1 = new GameObject("mesh " + mesh_index).transform;
                tr1.SetParent(tr);
                tr1.localPosition = Vector3.zero;
                tr1.localRotation = Quaternion.identity;
                tr1.localScale = Vector3.one;

                tr1.gameObject.AddComponent<MeshFilter>().sharedMesh = meshes[mesh_index];
                tr1.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mesh2mat[mesh_index];
                mesh_i++;
            }
        }
        foreach (var child in ReadArrayOfPtr<AiNode>(node.Children, node.NumChildren))
            Recurse(tr, child);
    }

    void ImportFile(string file, Action<IntPtr> process)
    {
        IntPtr ptr = IntPtr.Zero;
        IntPtr fileIO = IntPtr.Zero;

        //Only do file checks if not using a custom IOSystem
        if (String.IsNullOrEmpty(file) || !File.Exists(file))
        {
            throw new FileNotFoundException("Filename was null or could not be found", file);
        }

        try
        {
            ptr = AssimpLibrary.Instance.ImportFile(file, Assimp.PostProcessSteps.MakeLeftHanded, fileIO, IntPtr.Zero);

            if (ptr == IntPtr.Zero)
                throw new Assimp.AssimpException("Error importing file: " + AssimpLibrary.Instance.GetErrorString());

            //TransformScene(ptr);
            //if (postProcessFlags != PostProcessSteps.None)
            //    ptr = AssimpLibrary.Instance.ApplyPostProcessing(ptr, postProcessFlags);

            process(ptr);
        }
        finally
        {
            //CleanupImport();

            if (ptr != IntPtr.Zero)
            {
                AssimpLibrary.Instance.ReleaseImport(ptr);
            }
        }
    }
}
