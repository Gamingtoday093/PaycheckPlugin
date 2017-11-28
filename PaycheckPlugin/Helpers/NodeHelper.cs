﻿using System.Linq;
using SDG.Unturned;

namespace PhaserArray.PaycheckPlugin.Helpers
{
	public class NodeHelper
	{
		public static bool Exists(string search)
		{
			search = search.ToLower();
			return LevelNodes.nodes.Where(node => node.type == ENodeType.LOCATION).Any(node => ((LocationNode)node).name.ToLower().Contains(search));
		}
	}
}
