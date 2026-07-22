using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PCG.Sweep;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

public static class Task_20260722_360000
{
	private const int Layer = 31;
	private const int RenderSize = 1200;
	private const string Folder = "Temp/PatchDiag";
	private const int MaxVerts = 2000000;
	private const float Step = 1f;
	private const float MaxStep = 8f;
	private const float HeightOffset = 0.5f;

	public static async Task<string> Run()
	{
		await Task.Yield();
		Directory.CreateDirectory(Folder);
		var report = new List<string>();

		report.Add(Render("B_SharpV", new[] { Corner(new float3(-40, 0, 20), new float3(-2, 0, -30), new float3(40, 0, 20)) }, 4f, true));
		report.Add(Render("B_SlopedV", new[] { Corner(new float3(-40, 8, 20), new float3(-2, 0, -30), new float3(40, 8, 20)) }, 4f, true));
		report.Add(Render("B_Cross", new[]
		{
			Line(new float3(-30, 0, 0), new float3(30, 0, 0)),
			Line(new float3(0, 0, -30), new float3(0, 0, 30))
		}, 4f, false));

		return string.Join(" ;; ", report);
	}

	private static string Render(string label, Spline[] splines, float width, bool angled)
	{
		var temp = new List<UnityEngine.Object>();
		try
		{
			float maxAngleRad = math.radians(5f);
			float halfWidth = width * 0.5f;
			var list = new List<Spline>(splines);
			var snap = BuildSnapshot(list, halfWidth, Step);
			int vpr = 2;

			Type splitterT = FindType("PCG.Sweep.SweepRibbonSplitter");
			MethodInfo split = splitterT.GetMethod("Split", BindingFlags.NonPublic | BindingFlags.Static);
			MethodInfo buildFrames = FindType("PCG.Sweep.SweepNetworkFrames").GetMethod("BuildRangeFrames", BindingFlags.NonPublic | BindingFlags.Static);
			MethodInfo fanBuild = FindType("PCG.Sweep.SweepRibbonCornerFanBuilder").GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static);

			object result = split.Invoke(null, new object[] { snap, list, Step, CancellationToken.None, (Action)(() => { }) });
			var pieces = (System.Collections.IEnumerable)Field(result, "Pieces");
			var pieceList = new List<object>();
			foreach (var p in pieces) pieceList.Add(p);

			Shader sh = Shader.Find("HDRP/Unlit");
			Material greenMat = Mat(sh, new Color(0.78f, 0.72f, 0.55f));
			Material blueMat = Mat(sh, new Color(0.42f, 0.60f, 0.92f));
			Material wire = Mat(sh, new Color(0.05f, 0.05f, 0.06f));
			Material splineMat = Mat(sh, new Color(0.95f, 0.2f, 0.85f));
			temp.Add(greenMat); temp.Add(blueMat); temp.Add(wire); temp.Add(splineMat);

			Bounds b0 = default; bool found = false;
			int greenMeshes = 0, blueMeshes = 0, blueFan = 0, redPieces = 0, triCount = 0;

			foreach (var pc in pieceList)
			{
				int st = (int)Field(pc, "State");
				int spl = (int)Field(pc, "Spline");
				float a = (float)Field(pc, "StartStation");
				float b = (float)Field(pc, "EndStation");

				if (st == 1) { redPieces++; continue; }

				if (st == 2)
				{
					object fan = fanBuild.Invoke(null, new object[] { list[spl], a, b, snap, Step, CancellationToken.None, (Action)(() => { }) });
					SweepMeshData blueMesh = (SweepMeshData)fan;
					if (blueMesh.Vertices != null) blueFan++;
					if (blueMesh.Vertices == null) continue;
					blueMeshes++; triCount += blueMesh.Triangles.Length / 3;
					Spawn(blueMesh, blueMat, wire, temp, ref b0, ref found);
					continue;
				}

				float length = list[spl].GetLength();
				object frames = buildFrames.Invoke(null, new object[] { list[spl], a, b, length, a, Step, MaxStep, maxAngleRad, vpr, MaxVerts });
				if (frames == null) continue;
				var framesArr = (SweepFrame[])frames;
				if (framesArr.Length < 2) continue;
				SweepMeshData mesh = SweepMeshBuilder.Build(PieceSnapshot(snap, framesArr), 0, CancellationToken.None, () => { });
				if (mesh.Vertices == null) continue;
				greenMeshes++; triCount += mesh.Triangles.Length / 3;
				Spawn(mesh, greenMat, wire, temp, ref b0, ref found);
			}

			for (int s = 0; s < list.Count; s++) SplineLine(list[s], splineMat, temp, ref b0, ref found);

			string stats = "g=" + greenMeshes + " b=" + blueMeshes + "(fan=" + blueFan + ") r=" + redPieces;
			if (!found) return label + ": NO MESH " + stats;

			GameObject camObj = new GameObject("C"); camObj.hideFlags = HideFlags.HideAndDontSave; temp.Add(camObj);
			Camera cam = camObj.AddComponent<Camera>();
			cam.cullingMask = 1 << Layer; cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0.30f, 0.30f, 0.32f);
			float ext = Mathf.Max(b0.size.x, b0.size.z);
			if (angled)
			{
				cam.orthographic = false; cam.fieldOfView = 35f;
				Vector3 dir = new Vector3(0.15f, 0.75f, -0.64f).normalized;
				cam.transform.position = b0.center + dir * ext * 2.2f;
				cam.transform.rotation = Quaternion.LookRotation(b0.center - cam.transform.position, Vector3.up);
			}
			else
			{
				cam.orthographic = true; cam.orthographicSize = Mathf.Max(ext * 0.58f, 5f);
				cam.transform.position = b0.center + Vector3.up * 200f;
				cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
			}
			cam.nearClipPlane = 0.01f; cam.farClipPlane = 900f;
			string path = Folder + "/" + label + ".png";
			Capture(cam, path);
			return label + ": " + stats + " tris=" + triCount + " -> " + path;
		}
		catch (Exception e)
		{
			Exception inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
			return label + ": EXCEPTION " + inner.GetType().Name + " " + inner.Message + " @ " + (inner.StackTrace ?? "").Split('\n')[0];
		}
		finally
		{
			for (int i = temp.Count - 1; i >= 0; i--) if (temp[i] != null) UnityEngine.Object.DestroyImmediate(temp[i]);
		}
	}

	private static SweepSnapshot PieceSnapshot(SweepSnapshot src, SweepFrame[] frames)
	{
		return new SweepSnapshot
		{
			ProfilePoints = src.ProfilePoints, ProfileUs = src.ProfileUs, ProfileSegments = src.ProfileSegments, ProfileClosed = src.ProfileClosed,
			Frames = new[] { frames }, SplineClosed = new[] { false },
			WidthLut = src.WidthLut, HeightLut = src.HeightLut, TwistLut = src.TwistLut,
			Terrain = src.Terrain, MaxLateralExtent = src.MaxLateralExtent, UvScale = src.UvScale, HeightOffset = src.HeightOffset,
			CapStartFlags = new[] { false }, CapEndFlags = new[] { false }, Collider = false, Name = "T"
		};
	}

	private static void SplineLine(Spline spline, Material m, List<UnityEngine.Object> temp, ref Bounds b, ref bool found)
	{
		float length = spline.GetLength(); int n = Mathf.Max(2, Mathf.CeilToInt(length / 0.5f));
		var pts = new Vector3[n + 1];
		for (int q = 0; q <= n; q++) { float t = Mathf.Clamp01(spline.ConvertIndexUnit(length * q / n, PathIndexUnit.Distance, PathIndexUnit.Normalized)); pts[q] = (Vector3)spline.EvaluatePosition(t) + Vector3.up * 0.15f; }
		var wm = new Mesh { hideFlags = HideFlags.HideAndDontSave };
		var idx = new int[n * 2]; int d = 0;
		for (int q = 0; q < n; q++) { idx[d++] = q; idx[d++] = q + 1; }
		wm.vertices = pts; wm.SetIndices(idx, MeshTopology.Lines, 0); temp.Add(wm);
		GameObject g = new GameObject("SP"); g.hideFlags = HideFlags.HideAndDontSave; g.layer = Layer;
		g.AddComponent<MeshFilter>().sharedMesh = wm; g.AddComponent<MeshRenderer>().sharedMaterial = m; temp.Add(g);
		for (int q = 0; q <= n; q++) { if (!found) { b = new Bounds(pts[q], Vector3.zero); found = true; } else b.Encapsulate(pts[q]); }
	}

	private static void Spawn(SweepMeshData data, Material surface, Material wireMat, List<UnityEngine.Object> temp, ref Bounds b, ref bool found)
	{
		var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = data.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16, vertices = data.Vertices, uv = data.Uvs, triangles = data.Triangles };
		mesh.RecalculateNormals(); temp.Add(mesh);
		for (int i = 0; i < data.Vertices.Length; i++) { if (!found) { b = new Bounds(data.Vertices[i], Vector3.zero); found = true; } else b.Encapsulate(data.Vertices[i]); }
		GameObject s = new GameObject("S"); s.hideFlags = HideFlags.HideAndDontSave; s.layer = Layer;
		s.AddComponent<MeshFilter>().sharedMesh = mesh; s.AddComponent<MeshRenderer>().sharedMaterial = surface;
		temp.Add(s);
		var lines = new int[data.Triangles.Length * 2]; int d = 0;
		for (int i = 0; i + 2 < data.Triangles.Length; i += 3) { int x = data.Triangles[i], y = data.Triangles[i + 1], z = data.Triangles[i + 2];
			lines[d++] = x; lines[d++] = y; lines[d++] = y; lines[d++] = z; lines[d++] = z; lines[d++] = x; }
		var wm = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = mesh.indexFormat, vertices = data.Vertices }; wm.SetIndices(lines, MeshTopology.Lines, 0, true); temp.Add(wm);
		GameObject w = new GameObject("W"); w.hideFlags = HideFlags.HideAndDontSave; w.layer = Layer; w.transform.position = Vector3.up * 0.03f;
		w.AddComponent<MeshFilter>().sharedMesh = wm; w.AddComponent<MeshRenderer>().sharedMaterial = wireMat; temp.Add(w);
	}

	private static Material Mat(Shader sh, Color c)
	{
		var m = new Material(sh); m.hideFlags = HideFlags.HideAndDontSave;
		m.SetColor("_UnlitColor", c); m.SetColor("_BaseColor", c); m.SetColor("_Color", c); m.SetColor("_EmissiveColor", c); m.SetFloat("_EmissiveExposureWeight", 0f); return m;
	}
	private static void Capture(Camera cam, string path)
	{
		var tex = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
		var img = new Texture2D(RenderSize, RenderSize, TextureFormat.RGB24, false);
		var prev = RenderTexture.active;
		try { cam.targetTexture = tex; cam.Render(); RenderTexture.active = tex; img.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0); img.Apply(); File.WriteAllBytes(path, img.EncodeToPNG()); }
		finally { cam.targetTexture = null; RenderTexture.active = prev; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(img); }
	}
	private static Spline Line(float3 a, float3 b) { var s = new Spline(); s.Add(new BezierKnot(a), TangentMode.Linear); s.Add(new BezierKnot(b), TangentMode.Linear); return s; }
	private static Spline Corner(float3 a, float3 b, float3 c) { var s = new Spline(); s.Add(new BezierKnot(a), TangentMode.Linear); s.Add(new BezierKnot(b), TangentMode.Linear); s.Add(new BezierKnot(c), TangentMode.Linear); return s; }
	private static SweepSnapshot BuildSnapshot(List<Spline> splines, float halfWidth, float step)
	{
		var frames = new SweepFrame[splines.Count][]; var closed = new bool[splines.Count];
		for (int i = 0; i < splines.Count; i++) { frames[i] = BuildFrames(splines[i], step); closed[i] = splines[i].Closed; }
		var w = new float[256]; var h = new float[256]; var tw = new float[256];
		for (int i = 0; i < 256; i++) { w[i] = 1f; h[i] = 1f; tw[i] = 0f; }
		return new SweepSnapshot {
			ProfilePoints = new[] { new float2(-halfWidth, 0), new float2(halfWidth, 0) },
			ProfileUs = new[] { 0f, 1f }, ProfileSegments = new[] { 0, 1 }, ProfileClosed = false,
			Frames = frames, SplineClosed = closed, WidthLut = w, HeightLut = h, TwistLut = tw,
			Terrain = null, MaxLateralExtent = halfWidth, UvScale = 0.25f, HeightOffset = HeightOffset,
			CapStartFlags = new bool[splines.Count], CapEndFlags = new bool[splines.Count], Collider = false, Name = "T" };
	}
	private static SweepFrame[] BuildFrames(Spline spline, float step)
	{
		float length = spline.GetLength(); int count = Mathf.Max(1, Mathf.CeilToInt(length / step));
		var frames = new SweepFrame[count + 1];
		for (int q = 0; q <= count; q++) {
			float distance = length * q / count;
			float t = Mathf.Clamp01(spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized));
			frames[q] = new SweepFrame { Position = spline.EvaluatePosition(t), Tangent = spline.EvaluateTangent(t), Up = spline.EvaluateUpVector(t), T = distance / length, Distance = distance };
		}
		return frames;
	}
	private static object Field(object o, string n) => o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
	private static Type FindType(string n) { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(n, false); if (t != null) return t; } return null; }
}
