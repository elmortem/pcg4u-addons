using System;
using System.Collections.Generic;

[Serializable]
public class SceneViewShotSettings
{
	public string OutputFolder;
	public int OffsetTop = 46;
	public int OffsetBottom = 0;
	public int OffsetLeft = 2;
	public int OffsetRight = 2;
	public List<SceneViewShotItem> Items = new List<SceneViewShotItem>();
}
