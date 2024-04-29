using System.Xml.Serialization;

namespace PhaserArray.PaycheckPlugin.Serialization
{
    public class PaycheckXPCap
    {
        public uint MinimumXP { get; set; }
        public float Modifier { get; set; }

        public PaycheckXPCap() { }
        public PaycheckXPCap(uint minimumXP, float modifier)
        {
            MinimumXP = minimumXP;
            Modifier = modifier;
        }
    }
}
