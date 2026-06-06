using System.Collections.Generic;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplineListPool
	{
		private static readonly object _lock = new object();
		private static readonly Stack<List<Spline>> _pool = new Stack<List<Spline>>();

		public static List<Spline> Rent(int capacity)
		{
			List<Spline> list = null;
			lock (_lock)
			{
				if (_pool.Count > 0)
					list = _pool.Pop();
			}

			if (list != null)
			{
				if (capacity > 0 && list.Capacity < capacity)
					list.Capacity = capacity;
				return list;
			}

			return capacity > 0 ? new List<Spline>(capacity) : new List<Spline>();
		}

		public static void Return(List<Spline> list)
		{
			if (list == null)
				return;
			list.Clear();
			lock (_lock)
			{
				if (_pool.Count < 50)
					_pool.Push(list);
			}
		}

		public static void Clear()
		{
			lock (_lock)
			{
				_pool.Clear();
			}
		}
	}
}


