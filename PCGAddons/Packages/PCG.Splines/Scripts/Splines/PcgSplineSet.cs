using System.Collections.Generic;
using PCG.Attributes;
using PCG.Splines.Utilities;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class PcgSplineSet : IPcgAttributeData
	{
		public List<Spline> Splines = new();

		public PcgAttributeSet Attributes { get; } = new();

		public int Count => Splines.Count;

		public Spline this[int index]
		{
			get => Splines[index];
			set => Splines[index] = value;
		}

		public PcgSplineSet()
		{
		}

		public PcgSplineSet(int capacity)
		{
			Splines = new List<Spline>(capacity);
		}

		public PcgSplineSet(List<Spline> splines)
		{
			Splines = splines;
			Attributes.EnsureCount(splines.Count);
		}

		public List<Spline>.Enumerator GetEnumerator()
		{
			return Splines.GetEnumerator();
		}

		public void Add(Spline spline)
		{
			Splines.Add(spline);
			Attributes.AddRow();
		}

		public void AddRange(IEnumerable<Spline> splines)
		{
			foreach (var spline in splines)
			{
				Add(spline);
			}
		}

		public void AppendFrom(PcgSplineSet source, int sourceIndex)
		{
			Splines.Add(source.Splines[sourceIndex]);
			Attributes.AppendRow(source.Attributes, sourceIndex);
		}

		public void AppendFrom(PcgSplineSet source, int sourceIndex, Spline spline)
		{
			Splines.Add(spline);
			Attributes.AppendRow(source.Attributes, sourceIndex);
		}

		public void Append(PcgSplineSet source)
		{
			for (int i = 0; i < source.Splines.Count; i++)
			{
				AppendFrom(source, i);
			}
		}

		public void Clear()
		{
			Splines.Clear();
			Attributes.Clear();
		}

		public PcgSplineSet Clone()
		{
			var copy = new PcgSplineSet(Splines.Count);
			copy.Splines.AddRange(Splines);
			copy.Attributes.Append(Attributes);
			return copy;
		}

		public bool IsValid()
		{
			return Attributes.Count == Splines.Count;
		}

		public int GetContentHash()
		{
			unchecked
			{
				int hash = Splines.Count;
				for (int i = 0; i < Splines.Count; i++)
				{
					hash = (hash * 397) ^ SplinesUtility.GetContentHash(Splines[i]);
				}

				hash = (hash * 397) ^ Attributes.GetContentHash();
				return hash;
			}
		}
	}
}
