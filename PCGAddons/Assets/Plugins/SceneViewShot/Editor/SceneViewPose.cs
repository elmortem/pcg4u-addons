using System;
using UnityEngine;

[Serializable]
public struct SceneViewPose
{
	public Vector3 Pivot;
	public Quaternion Rotation;
	public float Size;
	public bool Orthographic;
}
