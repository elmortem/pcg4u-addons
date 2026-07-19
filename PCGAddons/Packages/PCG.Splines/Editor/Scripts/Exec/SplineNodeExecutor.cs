using System.Collections.Generic;
using PCG.Exec;
using PCG.Splines.Tools;
using PCG.Splines.Utilities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace PCG.Splines
{
	public class SplineNodeExecutor : PcgSyncPreviewNodeExecutor<SplineNode>
	{
		public PcgOutput<List<Spline>> Results;

		private SplineContainer _editContainer;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;

		public Object SerializationOwner
		{
			get
			{
				if (Graph != null && Graph.OwnerExecutor != null)
					return Graph.OwnerExecutor.Data.SubGraph;

				return Graph != null ? Graph.Host as Object : null;
			}
		}

		public void SetEditContainer(SplineContainer container)
		{
			_editContainer = container;
		}

		protected override void DoCompute()
		{
			Results.Value = Data.Splines;
		}

		public override int GetVersionSalt()
		{
			unchecked
			{
				int hash = 17;
				var splines = Data.Splines;
				hash = (hash * 397) ^ (splines != null ? splines.Count : 0);

				if (splines != null)
				{
					for (int s = 0; s < splines.Count; s++)
					{
						var spline = splines[s];
						if (spline == null)
							continue;

						hash = (hash * 397) ^ spline.Count;
						hash = (hash * 397) ^ spline.Closed.GetHashCode();

						for (int k = 0; k < spline.Count; k++)
						{
							var knot = spline[k];
							hash = (hash * 397) ^ knot.Position.GetHashCode();
							hash = (hash * 397) ^ knot.TangentIn.GetHashCode();
							hash = (hash * 397) ^ knot.TangentOut.GetHashCode();
							hash = (hash * 397) ^ knot.Rotation.GetHashCode();
							hash = (hash * 397) ^ (int)spline.GetTangentMode(k);
							hash = (hash * 397) ^ spline.GetAutoSmoothTension(k).GetHashCode();
						}

						foreach (var key in spline.GetFloatDataKeys())
							hash = (hash * 397) ^ SplineHashUtility.StringHash(key);
						foreach (var key in spline.GetFloat4DataKeys())
							hash = (hash * 397) ^ SplineHashUtility.StringHash(key);
						foreach (var key in spline.GetIntDataKeys())
							hash = (hash * 397) ^ SplineHashUtility.StringHash(key);
						foreach (var key in spline.GetObjectDataKeys())
							hash = (hash * 397) ^ SplineHashUtility.StringHash(key);
					}
				}

				hash = (hash * 397) ^ SplineHashUtility.LinksHash(Data.Links);
				return hash;
			}
		}

		public void WriteBack(SplineContainer container, bool withUndo)
		{
			var owner = SerializationOwner;
			if (withUndo && owner != null)
				Undo.RegisterCompleteObjectUndo(owner, "Edit Spline Node");

			ApplyContainer(container);

			if (owner != null)
				EditorUtility.SetDirty(owner);

			OnParametersChanged();
		}

		public void CommitStop(SplineContainer container, List<Spline> snapshot, KnotLinkCollection snapshotLinks)
		{
			var owner = SerializationOwner;

			Data.Splines = snapshot;
			Data.Links = snapshotLinks;

			if (owner != null)
				Undo.RegisterCompleteObjectUndo(owner, "Edit Spline Node");

			ApplyContainer(container);

			if (owner != null)
				EditorUtility.SetDirty(owner);

			OnParametersChanged();
		}

		public void PopulateContainer(SplineContainer container)
		{
			var copies = SplineCopyUtility.CopySplines(Data.Splines);
			if (copies.Count == 0)
				copies.Add(new Spline());

			container.Splines = copies;
			SplineCopyUtility.RestoreLinks(Data.Links, container.KnotLinkCollection);
		}

		private void ApplyContainer(SplineContainer container)
		{
			float4x4 matrix = container.transform.localToWorldMatrix;
			var links = SplineCopyUtility.CopyLinks(container.KnotLinkCollection);
			var source = container.Splines;
			var splines = new List<Spline>(source.Count);
			var dropped = new List<int>();

			for (int i = 0; i < source.Count; i++)
			{
				var spline = source[i];
				if (spline == null || spline.Count == 0)
				{
					dropped.Add(i);
					continue;
				}

				splines.Add(SplineCopyUtility.CopySpline(spline, matrix));
			}

			for (int i = dropped.Count - 1; i >= 0; i--)
				links.SplineRemoved(dropped[i]);

			Data.Splines = splines;
			Data.Links = links;
		}

		public override void DrawPreview(Transform transform)
		{
			if (_editContainer != null)
				return;

			var gizmosOptions = GetGizmosOptions();
			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value);
		}
	}
}
