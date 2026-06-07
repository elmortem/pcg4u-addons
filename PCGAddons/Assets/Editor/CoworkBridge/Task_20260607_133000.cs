using System.Text;
using UnityEngine;
using UnityEditor;

public static class Task_20260607_133000
{
	public static string Run()
	{
		var sv = SceneView.lastActiveSceneView;
		var cam = sv.camera;
		Debug.Log("cam pos=" + cam.transform.position + " fwd=" + cam.transform.forward + " pivot=" + sv.pivot + " size=" + sv.size);
		Debug.Log("cam orthographic=" + cam.orthographic + " fov=" + cam.fieldOfView + " near=" + cam.nearClipPlane + " far=" + cam.farClipPlane);

		Matrix4x4 proj = cam.projectionMatrix;
		Matrix4x4 w2c = cam.worldToCameraMatrix;
		Debug.Log("projectionMatrix:\n" + proj);
		Matrix4x4 vp = proj * w2c;

		Vector4 left = vp.GetRow(3) + vp.GetRow(0);
		Vector4 right = vp.GetRow(3) - vp.GetRow(0);
		Vector4 bottom = vp.GetRow(3) + vp.GetRow(1);
		Vector4 top = vp.GetRow(3) - vp.GetRow(1);

		Vector3 testPoint = sv.pivot;
		var sb = new StringBuilder();
		sb.Append("manual planes test for pivot: ");
		sb.Append("L=" + Dist(left, testPoint).ToString("F3"));
		sb.Append(" R=" + Dist(right, testPoint).ToString("F3"));
		sb.Append(" B=" + Dist(bottom, testPoint).ToString("F3"));
		sb.Append(" T=" + Dist(top, testPoint).ToString("F3"));
		Debug.Log(sb.ToString());

		var planes = GeometryUtility.CalculateFrustumPlanes(cam);
		var sb2 = new StringBuilder("GeometryUtility test for pivot: ");
		for (int i = 0; i < planes.Length; i++)
		{
			sb2.Append(i + "=" + planes[i].GetDistanceToPoint(testPoint).ToString("F3") + " ");
		}
		Debug.Log(sb2.ToString());

		Vector3 inFront = cam.transform.position + cam.transform.forward * 10f;
		sb = new StringBuilder("manual planes test for point 10m in front: ");
		sb.Append("L=" + Dist(left, inFront).ToString("F3"));
		sb.Append(" R=" + Dist(right, inFront).ToString("F3"));
		sb.Append(" B=" + Dist(bottom, inFront).ToString("F3"));
		sb.Append(" T=" + Dist(top, inFront).ToString("F3"));
		Debug.Log(sb.ToString());

		return "Frustum test complete";
	}

	private static float Dist(Vector4 plane, Vector3 p)
	{
		float mag = Mathf.Sqrt(plane.x * plane.x + plane.y * plane.y + plane.z * plane.z);
		return (plane.x * p.x + plane.y * p.y + plane.z * p.z + plane.w) / mag;
	}
}
