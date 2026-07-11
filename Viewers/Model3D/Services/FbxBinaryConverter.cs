using System.Numerics;
using System.Text;

namespace MauiMultimedia.Viewers.Model3D.Services;

/// <summary>
/// 手动解析 FBX 6.x 二进制格式，直接输出 GLB（glTF 2.0 Binary）。
/// 纯 C# 实现，无原生依赖。
/// </summary>
public static class FbxBinaryConverter
{
    private sealed class FbxNode { public string Name = ""; public List<object?> Props = new(); public List<FbxNode> Kids = new(); }
    private sealed class MeshData { public string Name = ""; public List<Vector3> Pos = new(); public List<Vector3> Norm = new(); public List<Vector2> UV = new(); public List<int> Idx = new(); }

    public static string? ConvertToGlb(string filePath, string outputPath)
    {
        var data = File.ReadAllBytes(filePath);
        if (data.Length < 27) return null;
        var root = ParseNodes(data, 27, data.Length);
        var objs = FindChild(root, "Objects");
        if (objs == null) return null;
        var meshes = ExtractMeshes(objs);
        if (meshes.Count == 0) return null;

        var binStream = new MemoryStream();
        var w = new BinaryWriter(binStream);
        int posOff = 0, normOff = 0, uvOff = 0, idxOff = 0;

        posOff = (int)binStream.Position;
        foreach (var m in meshes) foreach (var v in m.Pos) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        normOff = (int)binStream.Position;
        foreach (var m in meshes) foreach (var v in m.Norm) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        uvOff = (int)binStream.Position;
        foreach (var m in meshes) foreach (var v in m.UV) { w.Write(v.X); w.Write(v.Y); }
        idxOff = (int)binStream.Position;
        foreach (var m in meshes) foreach (var i in m.Idx) w.Write((ushort)i);
        w.Flush();

        var binData = binStream.ToArray();
        int binLen = (binData.Length + 3) & ~3;
        int totalVerts = meshes.Sum(m => m.Pos.Count);

        var json = new StringBuilder();
        json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"MauiMM_Fbx6\"},\"scene\":0,\"scenes\":[{\"nodes\":[");
        json.Append(string.Join(",", Enumerable.Range(0, meshes.Count)));
        json.Append("]}],\"meshes\":[");
        for (int mi = 0; mi < meshes.Count; mi++)
        {
            if (mi > 0) json.Append(",");
            json.Append("{\"primitives\":[{\"attributes\":{");
            json.Append("\"POSITION\":" + (mi * 3));
            json.Append(",\"NORMAL\":" + (mi * 3 + 1));
            if (meshes[mi].UV.Count > 0) json.Append(",\"TEXCOORD_0\":" + (mi * 3 + 2));
            json.Append("},\"indices\":" + (meshes.Count * 3 + mi) + "}]}");
        }
        json.Append("],\"accessors\":[");
        for (int mi = 0; mi < meshes.Count; mi++)
        {
            if (mi > 0) json.Append(",");
            json.Append("{\"bufferView\":0,\"componentType\":5126,\"count\":" + meshes[mi].Pos.Count + ",\"type\":\"VEC3\"}");
            json.Append(",{\"bufferView\":1,\"componentType\":5126,\"count\":" + meshes[mi].Pos.Count + ",\"type\":\"VEC3\"}");
            json.Append(",{\"bufferView\":2,\"componentType\":5126,\"count\":" + (meshes[mi].UV.Count > 0 ? meshes[mi].Pos.Count : 0) + ",\"type\":\"VEC2\"}");
        }
        for (int mi = 0; mi < meshes.Count; mi++)
            json.Append(",{\"bufferView\":3,\"componentType\":5123,\"count\":" + meshes[mi].Idx.Count + ",\"type\":\"SCALAR\"}");
        json.Append("],\"bufferViews\":[");
        json.Append("{\"buffer\":0,\"byteOffset\":" + posOff + ",\"byteLength\":" + (totalVerts * 12) + ",\"target\":34962}");
        json.Append(",{\"buffer\":0,\"byteOffset\":" + normOff + ",\"byteLength\":" + (totalVerts * 12) + ",\"target\":34962}");
        json.Append(",{\"buffer\":0,\"byteOffset\":" + uvOff + ",\"byteLength\":" + (totalVerts * 8) + ",\"target\":34962}");
        json.Append(",{\"buffer\":0,\"byteOffset\":" + idxOff + ",\"byteLength\":" + (meshes.Sum(m => m.Idx.Count) * 2) + ",\"target\":34963}");
        json.Append("],\"buffers\":[{\"byteLength\":" + binLen + "}],\"nodes\":[");
        for (int mi = 0; mi < meshes.Count; mi++)
        {
            if (mi > 0) json.Append(",");
            json.Append("{\"mesh\":" + mi + ",\"name\":\"" + JsonEsc(meshes[mi].Name) + "\"}");
        }
        json.Append("]}");

        var jsonBytes = Encoding.UTF8.GetBytes(json.ToString());
        int jsonLen = (jsonBytes.Length + 3) & ~3;
        int totalLen = 12 + 8 + jsonLen + 8 + binLen;

        using var fs = new FileStream(outputPath, FileMode.Create);
        var bw = new BinaryWriter(fs);
        bw.Write(0x46546C67); bw.Write(2); bw.Write(totalLen);
        bw.Write(jsonLen); bw.Write(0x4E4F534A); bw.Write(jsonBytes);
        for (int i = jsonBytes.Length; i < jsonLen; i++) bw.Write((byte)0x20);
        bw.Write(binLen); bw.Write(0x004E4942); bw.Write(binData);
        for (int i = binData.Length; i < binLen; i++) bw.Write((byte)0);
        return outputPath;
    }

    private static string JsonEsc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static List<FbxNode> ParseNodes(byte[] d, int start, int limit)
    {
        var nodes = new List<FbxNode>();
        int pos = start;
        while (pos + 13 <= limit)
        {
            int endOff = (int)BitConverter.ToUInt32(d, pos);
            if (endOff == 0 || endOff > d.Length || endOff <= pos) break;
            int numProps = (int)BitConverter.ToUInt32(d, pos + 4);
            int nameLen = d[pos + 12];
            if (nameLen <= 0 || nameLen > 255) break;
            string name = Encoding.ASCII.GetString(d, pos + 13, nameLen);
            var node = new FbxNode { Name = name };
            int pp = pos + 13 + nameLen;
            for (int i = 0; i < numProps && pp < endOff; i++) { var (v, l) = RdProp(d, pp); node.Props.Add(v); pp += l; }
            if (pp < endOff) node.Kids = ParseNodes(d, pp, endOff);
            nodes.Add(node);
            pos = endOff;
        }
        return nodes;
    }

    private static (object? v, int len) RdProp(byte[] d, int p)
    {
        char t = (char)d[p]; p++;
        switch (t)
        {
            case 'Y': return ((short)BitConverter.ToInt16(d, p), 3);
            case 'C': return (BitConverter.ToUInt32(d, p) != 0, 5);
            case 'I': return (BitConverter.ToInt32(d, p), 5);
            case 'U': return ((int)BitConverter.ToUInt32(d, p), 5);
            case 'F': return (BitConverter.ToSingle(d, p), 5);
            case 'D': return (BitConverter.ToDouble(d, p), 9);
            case 'L': return (BitConverter.ToInt64(d, p), 9);
            case 'S': { int l = (int)BitConverter.ToUInt32(d, p); return (l > 0 ? Encoding.ASCII.GetString(d, p + 4, l) : "", 5 + l); }
            case 'R': { int l = (int)BitConverter.ToUInt32(d, p); var b = new byte[l]; if (l > 0) Buffer.BlockCopy(d, p + 4, b, 0, l); return (b, 5 + l); }
            case 'b': case 'c': case 'd': case 'f': case 'i': case 'l':
            {
                int al = (int)BitConverter.ToUInt32(d, p), enc = (int)BitConverter.ToUInt32(d, p + 4), cl = (int)BitConverter.ToUInt32(d, p + 8);
                int es = t == 'd' ? 8 : (t == 'f' || t == 'i' || t == 'l') ? 4 : 1; if (t == 'l') es = 8;
                int total = 12 + (enc == 0 ? al * es : cl); var arr = new byte[total]; Buffer.BlockCopy(d, p, arr, 0, total); return (arr, total);
            }
            default: return (null, 1);
        }
    }

    private static FbxNode? FindChild(List<FbxNode> nodes, string name)
    {
        foreach (var n in nodes) { if (n.Name == name) return n; var f = FindChild(n.Kids, name); if (f != null) return f; }
        return null;
    }

    private static List<MeshData> ExtractMeshes(FbxNode objs)
    {
        var m = new List<MeshData>();
        void walk(List<FbxNode> ns) { foreach (var n in ns) { if (n.Name == "Geometry") m.Add(PrsGeo(n)); walk(n.Kids); } }
        walk(objs.Kids);
        return m;
    }

    private static MeshData PrsGeo(FbxNode g)
    {
        var m = new MeshData { Name = GStr(g, 0) ?? "mesh" };
        foreach (var c in g.Kids)
        {
            switch (c.Name)
            {
                case "Vertices": var vs = GetF64(c, 0); for (int i = 0; i + 2 < vs.Length; i += 3) m.Pos.Add(new Vector3((float)vs[i], (float)vs[i + 1], (float)vs[i + 2])); break;
                case "PolygonVertexIndex": var ia = GetI32(c, 0); var fi = new List<int>(); for (int i = 0; i < ia.Length; i++) { int vi = ia[i]; bool eof = vi < 0; if (eof) vi = (-vi) - 1; fi.Add(vi); if (eof) { if (fi.Count >= 3) for (int j = 1; j < fi.Count - 1; j++) { m.Idx.Add(fi[0]); m.Idx.Add(fi[j]); m.Idx.Add(fi[j + 1]); } fi.Clear(); } } break;
                case "LayerElementNormal": foreach (var x in c.Kids) if (x.Name == "Normals") { var n = GetF64(x, 0); for (int i = 0; i + 2 < n.Length; i += 3) m.Norm.Add(new Vector3((float)n[i], (float)n[i + 1], (float)n[i + 2])); } break;
                case "LayerElementUV": foreach (var x in c.Kids) if (x.Name == "UV") { var u = GetF64(x, 0); for (int i = 0; i + 1 < u.Length; i += 2) m.UV.Add(new Vector2((float)u[i], (float)u[i + 1])); } break;
            }
        }
        if (m.Norm.Count < m.Pos.Count) GenN(m);
        return m;
    }

    private static void GenN(MeshData m)
    {
        m.Norm.Clear(); m.Norm.AddRange(Enumerable.Repeat(Vector3.UnitY, m.Pos.Count));
        var ns = new Vector3[m.Pos.Count]; var ct = new int[m.Pos.Count];
        for (int i = 0; i + 2 < m.Idx.Count; i += 3)
        {
            int i0 = m.Idx[i], i1 = m.Idx[i + 1], i2 = m.Idx[i + 2];
            var n = Vector3.Normalize(Vector3.Cross(m.Pos[i1] - m.Pos[i0], m.Pos[i2] - m.Pos[i0]));
            if (!float.IsNaN(n.X)) { ns[i0] += n; ns[i1] += n; ns[i2] += n; ct[i0]++; ct[i1]++; ct[i2]++; }
        }
        for (int i = 0; i < ns.Length; i++) { if (ct[i] > 0) { ns[i] /= ct[i]; ns[i] = Vector3.Normalize(ns[i]); } }
        m.Norm.Clear(); m.Norm.AddRange(ns);
    }

    private static string? GStr(FbxNode n, int idx) => idx < n.Props.Count ? n.Props[idx] as string : null;
    private static double[] GetF64(FbxNode n, int idx) { if (idx >= n.Props.Count || n.Props[idx] is not byte[] raw) return []; int c = (raw.Length - 12) / 8; if (c <= 0) return []; var r = new double[c]; Buffer.BlockCopy(raw, 12, r, 0, c * 8); return r; }
    private static int[] GetI32(FbxNode n, int idx) { if (idx >= n.Props.Count || n.Props[idx] is not byte[] raw) return []; int c = (raw.Length - 12) / 4; if (c <= 0) return []; var r = new int[c]; Buffer.BlockCopy(raw, 12, r, 0, c * 4); return r; }
}
