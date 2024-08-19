using System.Collections.Generic;

namespace Mp7251_Key_Reader
{
    public class PLCVariable
    {
        public string Name { get; set; } = "";
        public ushort NameSpaceIndex { get; set; } = 6;
        public byte KeyNumber { get; set; } = 0;
    }

    public class PLCVariables
    {
        public PLCVariable KeySwitch { get; set; } = new PLCVariable() { Name = "::AsGlobalPV:KeySwitch" };
        public List<PLCVariable> KeyMatrix { get; set; } = new List<PLCVariable>() {};
    }
}
