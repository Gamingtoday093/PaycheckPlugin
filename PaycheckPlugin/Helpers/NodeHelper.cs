using System.Linq;
using SDG.Unturned;

namespace PhaserArray.PaycheckPlugin.Helpers
{
	public class NodeHelper
	{
		public static bool Exists(string search)
		{
			return LocationDevkitNodeSystem.Get().GetAllNodes().Any(node => node.name.Contains(search));
		}
	}
}
