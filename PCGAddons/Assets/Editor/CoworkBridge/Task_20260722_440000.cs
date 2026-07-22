using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PCG.Sweep;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

public static class Task_20260722_440000
{
	private const int MaxVerts = 2000000;
	private const float Step = 1f;
	private const float MaxStep = 8f;

	public static async Task<string> Run()
	{
		await Task.Yield();
		var report = new List<string>();
		report.Add(Rings("roll0", 0f));
		report.Add(Rings("roll45", 45f));
		report.Add(Rings("roll90", 90f));
		report.Add(Rings("roll180", 180f));
		return string.Join(" ;; ", report);
	}

	private static string Rings(string label, float rollDeg)
	{
		try
		{
			var spline = new Spline();
			var k0 = new BezierKnot(new float3(-30, 6, 0));
			k0.Rotation = quaternion.identity;
			var k1 = new BezierKnot(new float3(30, 6, 0));
			k1.Rotation = quaternion.AxisAngle(new float3(1, 0, 0), math.radians(rollDeg));
			spline.Add(k0, TangentMode.Linear);
			spline.Add(k1, TangentMode.Linear);

			MethodInfo buildFrames = FindType("PCG.Sweep.SweepNetworkFrames").GetMethod("BuildRangeFrames", BindingFlags.NonPublic | BindingFlags.Static);
			float length = spline.GetLength();
			float maxAngleRad = math.radians(5f);
			object frames = buildFrames.Invoke(null, new object[] { spline, 0f, length, length, 0f, Step, MaxStep, maxAngleRad, 2, MaxVerts });
			var arr = (SweepFrame[])frames;

			float upTotal = 0f;
			for (int i = 0; i + 1 < arr.Length; i++)
			{
				float3 u0 = math.normalizesafe(arr[i].Up, new float3(0, 1, 0));
				float3 u1 = math.normalizesafe(arr[i + 1].Up, new float3(0, 1, 0));
				upTotal += math.degrees(math.acos(math.clamp(math.dot(u0, u1), -1f, 1f)));
			}

			return label + ": rings=" + arr.Length + " upSpanDeg=" + upTotal.ToString("F1");
		}
		catch (Exception e)
		{
			Exception inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
			return label + ": EX " + inner.GetType().Name + " " + inner.Message;
		}
	}

	private static Type FindType(string n) { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(n, false); if (t != null) return t; } return null; }
}
